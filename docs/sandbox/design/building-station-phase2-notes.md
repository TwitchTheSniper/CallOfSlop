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
  messages. **Protocol 285**, only because the part's port list is data both peers must share. A save made mid-fill resumes: the
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

## The user's answers (2026-11-13)

1. **A fill setting** at the station (full, none, a percentage) (not the recommendation).
2. **A setting at the station** for whether a build waits for fuel and ammo or finishes empty and says so.
3. **Batteries are charged from the station's power**, taking time.
4. **Ammo Containers and fitted magazines are filled** from bulk ammo in linked bins, following the fill setting.
5. **Gas tanks fill from linked bins too**, as well as through pipes (not the recommendation).
6. **Creative: tanks come full**, as built.

## Fill settings (2026-11-13, the answers built)

**Built.** Answers 1 to 5; 6 needed nothing (creative already came full; batteries too, since `Empty` is skipped there).

- **Fill setting:** `fillFuel`, `fillAmmo`, `fillCharge` (0-100 %, step 5, default 100) on the station part (part kit
  `build_station_parts`, CfgKey at the end), in the configure tool and as three sliders on the station window's new Fill row. The
  server takes the window's change with any tool in hand, but only for these four settings (`ConstructStation.WindowSets`, checked
  in `ConstructEdits.Configure`).
- **Wait setting:** `fillWait`, a checkbox on the same row. On: a look that moves nothing keeps the job Building, the note reads
  "waits for its fill: ammo 170/330 rounds - ..." and the screen's row 4 WAITS FOR FILL. Off: Done with "not filled: ...", as before.
- **Ammo Containers and fitted magazines** filled from bulk ammo in linked bins, to `fillAmmo`, in the same 10-units-a-look budget.
- **Gas from bins:** a Fluid Tank whose blueprint fluid is hydrogen, helium or oxidiser takes what pipes don't give from bulk in the
  linked bins, 4 units a look at the Gas Compressor's 50 L a unit.
- **Batteries:** a survival build's batteries start empty (`Empty` clamps the charge as it does the fuel) and the fill stage moves
  charge from the station's construct at 50 kW plus its plants' output (`ChargeKW`, proposed); the note shows "charge 40/300 kJ".
- Protocol 292 (the part's settings list; no new bytes). Window tool: `station set KEY VALUE`; `station tanks` also prints the
  build's charge, magazines and Ammo Containers and the station's charge.

**Tried by hand** (window tool, Production Showcase at 140,0,-100, `timed`, a throwaway `--save-dir`):
- Gun Bench (Ammo Box, Box Magazine), 40 units of ammo in a bin: magazine 30/30 and box 300/300 at Done; with `fillAmmo 50`, 15/30
  and 150/300. With `fillWait 1` and 20 units: it held at 170/330 with the waits note and WAITS FOR FILL, then finished once 20
  more went in. (The bench's gun loads from its magazine while frozen, so the station refills the magazine it drew.)
- Lamp Post: battery 0/300 kJ with every part on; `fillCharge 80` gave 240/300 at Done, the station's charge 500 to 416 kJ.
  A world saved and loaded before the charge kept 0/300, then charged to 300.
- Two-Stage Rocket (bay 10 x 10 x 24 m), `fillFuel 50`, a pipe from the showcase's Fuel Tank and 100 oxidiser in a bin: its two
  fuel tanks 20/40 L each from the pipe (the showcase's tank 80 to 40 L), its two oxidiser tanks 20/40 L each from 2 units.
- Host and joined client (port 27999: another session's host held the default): the client set `fillWait` from its own window path
  (no configure tool), saw the waits note on the screen, the fill and Done, the same counts as the host.
- A world saved and loaded mid-fill (`station reload`, waiting at 170/330): the wait setting and the rounds kept; it went on to Done.
- `--check-construct-parts` 0, `--gen-parts all --check` 0 out of date. No self-test sections run or added.

**Not verified:** a cut pipe; an Ammo Container (no small shipped blueprint has one); a battery seen charging from the client
(the charge rides the body's state message, not checked for a frozen body); a station with no power left to charge from, with
the wait on; creative's charged batteries from a station; tick cost (the look walks the steps at 2 Hz, as before; no allocation
but the note string).

**New questions for the user**
1. **Gas units:** a bulk unit is 50 L, so a 40 L oxidiser tank at 50 % takes a whole unit and loses 30 L. Keep that (simple, as a
   Compressor's granularity), or **(recommended)** give the whole unit up to the tank's room, past the setting?
2. **Charge rate:** 50 kW plus the station construct's plants' output (a Battery in 6 s, a 1 MJ bank in about 5 hours with no plant).
   Keep it **(recommended, proposed)**, or tie it to the station's draw (150 W today) or a setting?
3. **Defaults:** fill 100 % and wait off (phase 2's behaviour). Should wait default to on **(recommended: a build without fuel can't
   drive out anyway)**, or stay off?
