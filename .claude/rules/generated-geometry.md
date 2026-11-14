---
paths:
  - "Source/PartKit/**"
  - "Source/Constructs/HullLoft*.cs"
  - "Source/Constructs/ShellLooks.cs"
  - "Source/VehicleKit/**"
  - "Source/Interiors/**"
  - "Source/Buildings/**"
  - "docs/sandbox/PARTKIT.md"
  - "docs/sandbox/INTERIORS.md"
  - "docs/sandbox/constructs/look.md"
  - "docs/sandbox/constructs/buildings.md"
---

# Generators (parts, hulls, vehicles, interiors, buildings): what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- **Generators, not outputs.** A generated file is fixed in its generator, never by editing the output, and a generator caps every amount by the file's limits (one recipe over `MaxUnits` makes the whole Assembling table refuse). A check's counts come from the generator's constants (`EngineKitParts.Grades`, `SandboxTiles.ResourceSlot`), not a number.
- Hull faces keep only convex corners; a face is a repeat only when every point lies on the other's plane; facets meeting at a point share a mitred offset (`HullLoft.Mitres`); a new shape kind joins `ZFight.Boxes` when it is added.
- A part's place rounds to the centimetre and `Put` moves a part up to its reach (0.2 m) onto a socket: socket offsets are whole centimetres, floors in a row sit on a half-metre lattice (`Deck(..., aligned: true)`), stand-offs keep half a centimetre extra (`HullLoft.TrimGap`), and keep-clear tests take the placing call's reach as margin.
- A generated part keeps its own faces apart by construction: drawn feet a few mm above the rails' plane, helper boxes a few mm off drawn faces, pieces meeting at a shared point stop short of it, scaled sections drop rows too thin for their facets (`CabLook.Clamped`), thick pieces over a bend are flat with seams. `--body-report` counts coplanar pairs inside every lofted part.
- Dressing goes on with `Onto(..., clear: true)`; turret and gear spots come from the bays as built (`BaySpots`); a refusal names its blocker in the rest frame (`Vehicle.Blocker`); judge shapes on real meshes (`--plan`, `--vehicle-shots`) and `VehicleChecks.Gaps`.
- Measure a data-wide correction against the old shape before merging (`--zfight-moves`). Steps never grow with passes, the amount comes from the shared patch, and rotated boxes are not axis-aligned ones.
- 2026-11-12: main went red on "machine tier looks" after machine look phase 3: `ModuleLook` scaled the drill collars' height by the tier size, but the bit's height is the same at every tier, so the Plasma Drill's tier-5 collar hung 1.6 m under the bit; the phase never ran that section. A look pass runs the section that checks looks, and a look box takes each axis's scale from the body it dresses.
- 2026-11-12: Wall clocks showed only their dark back disc: the part kit's `Box` silently dropped any box under 10 mm, and about 200 detail boxes in 16 generators were lost the same way (`PartScale.DetailMin` now grows a thin detail box to 16 mm). Never drop geometry silently; when a part looks bare, compare the generator's boxes with the written file.
- 2026-11-12: `--gen-parts all --check` said every generator was out of date on the Windows PC: the checkout is CRLF (`core.autocrlf=true`) and generators write LF. Regenerate, then `git diff --ignore-cr-at-eol --stat` (empty: no real change) and `git checkout -- .`; or run the check while the LF copies are there.
- 2026-11-12: the Panel Former's screen couldn't be touched: its board was a look box (`MachineLook.D`, detail), which the crosshair's ray passes, so Hands found nothing. A screen with buttons is mounted on a solid box; and the game reads data from `bin/`, so rebuild after `--gen-parts` before a window check.
- 2026-11-14: Karl had to crouch up every colony stair and walked into tables: the 1 m Straight Stair under its 1 m floor opening leaves a standing head 10 cm, and furniture was stood by pad sockets in the middle of rooms with no check that a person could get round. Buildings use the Wide Stair under its Wide Stair Floor, furniture goes on the `"wall"` socket, and a building blueprint passes `--check-building` (buildings.md "Laying out a building") before it merges.
- 2026-11-14: a Loading Hoist reaching 2.55 m out over the Requisitions dock moved the whole store 1.3 m back on the colony street (into a heating branch), because `ColonyBuildingPlace` stands a building by its bounds' middle. A piece that sticks out past a placed building's old outline comes with a matching shift of its `at` in Story/Opening/ColonyBuildings.json (or a world site's spot).
