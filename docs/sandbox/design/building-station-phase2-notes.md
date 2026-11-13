# Building Station phase 2: notes (2026-11-13)

## What phase 2 is, and why

No design names a Building Station phase 2. Phase 1's "Not in phase 1" list (production.md, "Building station") starts with
**arms and cranes**, which has its own six-phase design (`arms-and-cranes.md`), and the user hasn't answered its questions yet
(Q1: a scripted arm or a physical jointed arm; Q4: whether big parts need a lifter). The coordinator chose the next item that
touches only the station: **fluids and ammo filled from linked tanks and bins**. Arms and cranes wait on the user's answers to Q1
and Q4.

## What was built

- **Fitted empty (survival).** A part the station fits holds nothing: a fuel tank adds no fuel to the pool (`AddPart` and the
  first rebuild used to fill it), an ammo box has no rounds (it came with its `BoxRounds`), and a Fluid Tank holds none of its
  blueprint's fill. Before this, every station build came out with full tanks and boxes for free. In a creative world (machines
  untimed) they come full, as a spawned blueprint's do (`ConstructStation.FillsFree`).
- **Fluid in.** The station part has a fourth port, **Fluid in** (pipe, in), on the sorter's -x side beside Piece in 2: a steel
  flange with a white ring (part kit `build_station_parts`). Pipes laid to it from tanks on the station's construct are its fuel and
  fluid supply. A fuel tank as the source gives from its construct's pool, so a pipe from a generator's tank drains that generator
  (the player's choice).
- **The fill stage**, after the last link and before the finish, a look every half second: the build's fuel pool to full, its
  Fluid Tanks to their blueprint's fill (by the step's config: fluid and percentage; hot air, vacuum and exhaust are left out),
  its ammo boxes to full. Fuel and fluids come through the intact pipes into Fluid in (`ConstructFluid.Move`, 50 L a look); rounds
  come from bulk `ammo` in the linked bins (`ConstructCrafting.NetOf`, 10 units a look, 10 rounds a unit). The stage ends when
  everything is full or when a look moves nothing. The job is then done as before, and what wasn't filled is named on the screen
  and the look line: "not filled: fuel 0/40 L, ammo 50/300 rounds - pipe tanks to its Fluid in, ammo in a linked bin". While it
  fills, the state reads "filling its tanks".
- **Wire and saves:** no new bytes. The note and state ride `CBodyKind.Station`; fuel, rounds and tank fills ride their own
  messages. **Protocol 279**, only because the part's port list is data both peers must share. A save made mid-fill resumes: the
  job is Building with every part and link in place, so it goes straight to the fill stage.
- **Window tool:** `link pipe FROM > TO` (host: a pipe laid directly, 5 m over the machines, with the tool's checks but not its
  reach), `station tanks` (the build's fuel, ammo boxes and Fluid Tanks; the pipes into the station; the linked bins by `NAME#n`),
  `station await fitted|linked|done` (waits at most 100 s, then fails the run).

## Tried by hand (window tool, Production Showcase on the Proving Ground at 140,0,-100, `timed`)

- Swing Arm Buggy, a pipe from the showcase's second Fuel Tank: fuel 0.0/40.0 L with all 26 parts on, 40.0/40.0 L at Done; the
  showcase's pool went from 80 to 39.7 L.
- Hood Gun Buggy, the Mounted MG and Ammo Box handed in (nothing in the showcase makes them), the copper bin switched to 5 ammo
  once the links were in: Ammo Box 0/300 with all 27 parts on, 50/300 at Done, fuel 40/40. That run showed the note bug fixed
  below.
- Host and joined client: the client saw the pipe into Fluid in and the frozen build at 0.0 L fuel. The host's build reached
  17/26 before the 100 s await cap stopped the run, so the client never saw the fill or Done.
- `--check-construct-parts` 0, `--gen-parts all --check` 0 out of date. No self-test sections run or added.

## Not verified

- The note at Done after a partial fill: fixed after the run (it kept "filling: ..." because the note's key didn't include
  whether the last look moved anything); not run again.
- A Fluid Tank in a blueprint filled to its config (no shipped buggy has one); gases; a cut pipe; a save and load mid-fill; a
  client watching the fill and Done; creative's full tanks from a station.
- Tick cost: the fill look walks the steps once at 2 Hz, only in the fill stage; the only allocation is the note string, made
  when the counts change.

## Open questions for the user

1. **Fill to full, or as the blueprint has it?** Fuel and ammo go to full (what a spawned blueprint has) and Fluid Tanks to their
   saved fill. Should the station have a "fill" setting (full, none, a percentage)?
2. **Wait or finish?** The build finishes with empty tanks and says so, where a missing link stock makes it wait. Should it wait
   for fuel instead (it can't drive out without any)?
3. **Batteries** still come charged for free, as before. Should the station charge them from its own power?
4. **Ammo Containers and magazines** (bulk-ammo storage volumes and fitted magazines) are left as the blueprint loader leaves
   them. Fill them too?
5. **Gas from bins:** hydrogen, helium and oxidiser are bulk materials. Should the station fill gas tanks straight from linked
   bins, as a Gas Compressor does, instead of only through pipes?
6. **Creative:** tanks come full there. Is that right, or should a station always fill from what it is given?

## For the coordinator

- Nothing touched the logistics files. `Program.WindowShot.cs` has the new verbs (the `link` verb took a `pipe` form).
- `--check-construct-parts` lists about 80 `Loot.json: ... it has no recipe to show its tier` lines (Wiring Loom, Gear Set,
  Copper Coil and others, most likely the rows press phase 5 took off the Assembler). They don't count in the exit code and don't
  come from this branch.
