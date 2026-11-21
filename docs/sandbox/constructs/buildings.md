# How a building should look

Read [look.md](look.md) first: paint, finishes, lamps and the detail parts are there. This file is the style guide for
**buildings**: what every building needs, then the styles. A building is a construct blueprint from the part kit
(`Source/PartKit/Generators/BuildingParts*.cs`, `--gen-parts building_parts`); the older map buildings (`Buildings/`) are
flat-map things and don't stand on a planetoid.

The one style so far is Brutalism (the user, 2026-11-12: "Brutalism, which will contrast well with the cold. Old cold war
kind of vibes inside; the machines are old, light switches etc are old. Modelled with the intention of fully fitting their
role, based on architectural knowledge"). It was the Ministry of Slop's until the old factions and the story went
(2026-11-17 and 2026-11-21, docs/removals-notes.md), with the penal colony's street, the spaceport and the outposts. A new
style is written here, to the same headings, before building in it.

## Every building, whoever built it

1. **The job gives the shape.** Before any part goes down, write what the building is for and what that asks of it (who
   comes in, who never does, what must be kept dry, dark, warm or watched), and let that set the plan and the outline. The
   comment over a blueprint in the generator says it. A building that is "a room with furniture in" is not finished.
2. **It reads at 50 m and differs from its neighbours.** In a storm or at night a player sees an outline and some lamps.
   No two buildings of one street have the same outline: vary height, the roof, what sticks out and what is left open.
   One building of a group is the tall one and one has the thing nobody else has (a stack, an overhang, a dock).
3. **One style per faction.** A material, a palette of three (look.md "Rules of thumb"), a roof, a way of making windows
   and one signature. A faction's buildings are told from another's at a glance; they never share blueprints.
4. **The way in is marked and usable.** A pad is 0.5 m of plinth: every door a person uses has steps
   (`Concrete Entrance Steps 2 m`), a lamp, shelter over it and the building's name by it.
5. **Lit and switched.** A battery, lamps outside in the faction's colour, lamps inside on a switch a player can work.
6. **Fitted for the people in it.** Furniture stands where the job puts it (a counter between the public and the files),
   against walls, not in a row down the middle. Something in each room is somebody's.
7. **The place fits the climate.** On a cold world: small, high or narrow windows, doors under cover, a blank wall to
   the weather, the heat's stack showing.

## The site: what the buildings stand on and what joins them

A group of buildings is a place only when the ground under it was made for it and something runs from door to door. The
first site was the penal colony's, which went with it (2026-11-17, docs/removals-notes.md); its pieces stay in
`BuildingParts.Site.cs` (`Concrete Road Slab`, `Concrete Fence Panel 4 m`, `Sliding Gate Leaf 4 m`, the `Heating Main`
pieces, `Concrete Chimney 2.75 m`, the flagpole, `Concrete Notice Board 2 m`). Plan a faction's settlement to these
headings:

1. **One level.** A camp on frozen ground is founded on a bench: the land is cut and filled to one height (ground ops, a
   player's Flatten through `Game.Dig`, which saves and reaches a joiner) before anything is built, with a ramp from
   each edge to the land. No building stands on raw land at its own level.
2. **Zones, as the job has them.** A secure compound (the only fence), the public buildings in one row facing it, staff
   living to one side, work to the other, services downwind by the road, one road in.
3. **A drawn plan.** Pieces are laid at exact spots square with the site's plane, so they meet edge to edge.
4. **Hard ground where people walk** (`Concrete Road Slab`, 0.12 m proud of the bench).
5. **A covered way on the walk made every day.**
6. **Services above ground, and shown:** lagged mains on trestles, risers over a road, the tallest stack at the boiler.
7. **A place run by notice:** gate piers, a flag, notice boards.
8. **One fence, and a reason for each way through it.** A long thin body has a big radius, and the ring search for clear
   ground (`SandboxKitPiece`) keeps that far from it: anything placed after a fence must be given its spot.
9. **Every building is on the heat.**

No faction's settlement has a site yet.

## Ambience: what moves and sounds about a place

`Constructs/Ambience.json` names parts that do something wherever they are built, on any world
(`Source/Render/PartAmbience.cs`): looks and sound only, worked out on each PC, nothing sent or saved.

| Kind | What the part does | Now on |
| --- | --- | --- |
| `smoke` | Puffs from its top, bent by a storm's wind; `top`: only the highest of its name on a body | `Concrete Chimney 2.75 m` |
| `steam` | Vents for `burst` seconds every `every` | `Heating Valve Chamber` |
| `searchlight` | In a storm's dusk, a pool of light `width` m across sweeps the ground up to `reach` m out (the nearest one only: one of the frame's four lights) | `Tower Roof 2x2 m` |
| `tannoy` | Two chimes and a garbled announcement about every `every` seconds, heard within `reach` m (the mixer's "Storm wind" row) | `Flagpole Top 3.75 m` |

A cold storm also blows snow along the ground round the camera (the `drift` particle kind), on any world with a storm.
To give another faction's place life, name its parts in the file: no code. People are not this file's business.

Cost (measured 2026-11-13 in the agent container, a site in view): the world's bodies are looked through twice a
second, the frames between walk a short list (seven parts there): about 3 microseconds a frame plus 1 for the
searchlight, against 6.5 for the lamps' own code on the same machine; three rays every fifth of a second; no
allocation in a steady frame. Particles go into the one pool at the Particles setting's share.

Not done: a searchlight has no beam and no lit lamp head (only its pool on the ground); smoke rises along world +Y
(the particle pool's gravity), right at a pole and wrong far from it; no steam on the mains' pipes themselves; the
tannoy is one recording.

## Brutalism

Brutalism builds as a government that expects to be there for ever and has stopped spending: heavy raw concrete,
honest about its weight, a little too big for what goes on inside, and unchanged since it was poured. Against Glacier's
snow it is a row of grey masses with amber lamps.

| | |
| --- | --- |
| **Material** | Concrete, bare. Walls `#a29e94`, everything that carries or caps (lids, columns, piers, canopies, steps, slabs) weathered `#858179`, finish `concrete`. Never corrugated tin, never wood outside. The infirmary alone is whitewashed (`#d6dad3`). |
| **Accents** | Civil green `#2f5a3c` on doors and filing; navy `#1f2b45` signs; slate `#4b5157` on furniture and fuse boxes. |
| **Light** | Outside: `Bulkhead Light`, sodium amber `#ffb347`, under every canopy and overhang. Inside: `Strip Light`, a cold green-white `#e4f2dc`. Warm outside, cold inside: the wrong way round, as it would be. |
| **Roof** | Flat, always, with **the lid**: `Concrete Fascia` round every roof slab, 0.75 m deep and 0.5 m out, with its corners. A roof without its lid is not Brutalist. |
| **Windows** | Few and never at eye level: `Concrete Ribbon Wall` (a high strip under a hood) where people work, `Concrete Slot Wall` (tall 0.25 m slots) where things are kept. A wall with nothing to light is blank. |
| **Weight** | `Concrete Buttress` (stepped piers) on blank and long walls, `Concrete Fin` on fronts, at 2 m centres (a wall's `f0_5`, `f8_5`). Upper floors may oversail the ground floor on `Concrete Column`s; a roof may be thrown out with nothing under it. |
| **The way in** | `Concrete Canopy 2 m` over a door corner's doorway with a lamp under it and the name on its front, or the door under an overhang; `Concrete Entrance Steps 2 m` in front. |
| **Signature** | Each building is one plain mass, or two of different heights, with one bold move: an overhang, a tall hall, a stack, a dock, a cantilever. |
| **Signs** | Navy `Text Sign`s in capitals, in an official voice: a name, then an instruction nobody could follow. |

### Inside: the cold war

Nothing inside is newer than the building. The fittings are the part kit's (`InteriorParts.ColdWar.cs`):

- **Bakelite Switch**: a round brown light switch, a `switch` logic part. Every room's strip lights are wired to one by
  the door (`"wires"`), started on, so a player can turn a room's lights off with it.
- **Strip Light**, **Bulkhead Light**: mains lamps (lit from the building's battery, the fuse box high on a wall).
- **Cast Iron Radiator** under windows and by desks; **Wall Clock** (stopped) where people wait.
- **Filing Cabinet** (storage, four drawers), **Teleprinter**, **Tape Cabinet** (an old computer: reels and
  lamps). These, the Locker and the Gun Rack have a `"wall"` socket low on the back: hung on a wall's bottom face socket (`k8_0`) they stand on the
  floor against the wall, where a pad's own sockets (0, 1.75, 2 and 3.75 m) would leave them a metre out.
- **Service Counter 2 m / 1.5 m** (a wooden counter, closed side to the public, a shelf on the staff's; lengths chain on
  `+x`/`-x`), **Steel Desk**, **Waiting Bench 2 m** (a seat), **Iron Bed** (a seat that is a Bunk: a body wakes there),
  **Store Shelving 2 m** (storage): all on the `"wall"` socket, so they stand against a wall.
- With them, the older parts that fit: Locker, Parts Shelf, Medical Station, Bunk, Galley Stove, Tea Urn, Computer, Fax
  Machine, Phone Box, Pillar Box, Queue Post, Belisha Beacon, Employee of the Month. A Mess Table is a canteen's table,
  not a counter.

Colours inside: cream, civil green, cast-iron grey, brown Bakelite; `worn paint` on everything painted.

### Building in the style

The helpers in `BuildingParts.Brutalist.cs` do the repeated work: `Roof` and `Lid` (slabs and their fascias; `front:
false` where a taller part stands), `Porch` (canopy, lamp, name), `Steps`, `Piers` (fins or buttresses on a wall's face
sockets), `Outside` and `Strip` (lamps in the two colours), `Switch` (a Bakelite Switch wired to its lamps: a wire
reaches 8 m), `NameBoard`, `Notice`, `Warning` and `Sign` (the signs, below), `Door`. A blueprint holds up to 128 steps.

What the game's build check taught (the generator's mirror doesn't know what is in the way: run
`--check-construct-parts`, which builds every blueprint, after `--gen-parts building_parts` and a build):

- A canopy, a lamp or a sign on a wall's top row (`..._10`) is clear of the lid, which starts at the slab. A Text Sign
  on the top row hits a slab that runs out over it: put the name on the fascia's front (`f15`) there.
- A fin or a buttress takes its wall's three face sockets at that point; nothing else hangs there. Keep them off corner
  pieces (their plinth band would share a plane) and off a door's leg.
- Walls stand on walls for a tall hall (no floor between); a slab that meets a wall from outside at an upper storey's
  foot is why the corners' plinth band starts 4 mm up.
- The west wall upstairs over a `Concrete Wide Stair Floor`'s opening (x 0.25 to 2 m, z 0.5 to 4 m) is the stairwell's.
- Furniture on the `"wall"` socket: two on neighbouring sockets (`k7_0`, `k8_0`) overlap; use `k0`, `k8`, `k15`.
- A 4 by 4 m room is four corners, which take no fin or buttress: give it height or a thrown roof instead. `Lid(...,
  back: false)` leaves the +z side off where a roof runs into a taller wall; close the rest by hand.
- A stair in a 4 m wide room: `Concrete Wide Stair Floor` turned `RY180` at `Cell(15, slab, z + 31)` with the
  `Concrete Wide Stair 3 m` `R0` at `Cell(8, G, z + 16)` puts it along the east wall, rising to the back (the Barracks);
  the floor `R0` at `Cell(0, slab, z)` with the stair `RY180` at `Cell(7, G, z + 15)` puts it along the west wall,
  rising to the front (the Records Office). Its head ends at the wall: a person steps off it sideways.
- A tower's way up is a `Fixed Ladder 3 m` through a `Concrete Hatch Floor 4x4 m`'s hatch (x 1.5 to 2.5 m, z 1.5 to
  3.5 m): turned `RY180` at `Cell(8, G, 7)` on the pad's middle socket, its back to the hatch's front edge, climbed from
  under the hatch and stepped off forwards (a fixed ladder lands behind its top). Only a pad or a plain floor has middle
  sockets, so a second ladder can't stand on a hatch floor: one storey of shaft.
- A Notice Board holds three lines of seven letters (`PanelRender.Columns`): a longer notice is cut mid-word.

### Laying out a building

Karl's report (2026-11-14): the stairs had to be crouched and tables stood in the way. The rules that fixed it, which
`--check-building NAME[,NAME...]|all` checks (a standing body, 0.4 m round and 1.8 m tall, stepping up 0.55 m at most,
as the game walks it and as crews path; `BuildingWalkCheck.cs`, maps of each level in `building-walk.txt`):

1. **Stairs are Wide Stairs.** `Concrete/Steel/Wood Wide Stair 3 m` (1.75 m wide, 12 steps of 25 cm over 3.5 m) under a
   `... Wide Stair Floor` (an opening of 1.75 by 3.5 m). The old Straight Stair is 1 m wide under a 1 m opening: a
   standing head clears the slab's edge by 10 cm, so a player crouched. Leave 1 m clear at the foot, and a side to step
   off at the head.
2. **Order the rooms by who goes where.** The public's side by the door (a queue, a bench, the forms), the counter
   across between them and the staff's side, the staff's way round at one end of the counter (1.1 m at least) marked
   STAFF ONLY, the stair's foot on the staff's side. Sleeping away from the door; triage at it.
3. **Furniture stands against walls**, on the `"wall"` socket, never across a doorway, a stair's foot or head, or the
   room's one way through. A way between two things is 0.9 m at least (the people's paths need it), 1.1 m to pass
   without brushing.
4. **Everything used is reachable**: every seat, store, switch and item within 1.6 m of somewhere a person can
   stand and leave from. A switch over a stairwell is out of reach; put it at the stair's head.
5. **A doorway opens onto floor**, not onto a flight's head: in a shaft of stacked flights the ground floor's goes up the
   side away from the door.
6. **A fixed ladder joins its foot and the landing behind its top** (both need room to stand), as `ConstructRide.ClimbLadder`
   climbs it; a ship ladder walks as a stair.

Run it after `--gen-parts building_parts` and a build, with `--check-construct-parts`. It changes nothing; the exit code
is the number of buildings with a failure.

### Signs

Signwritten boards, not screens, where a place has no power to light a Text Sign: the part kit's `Sign Board 3 m`,
`Sign Board 2 m` and `Notice Board` (`ControlParts.cs`), whose painted colour is the board and whose words are drawn in
cream on a dark board, black on a light one, shrunk to fit and wrapped on the notice.

### Not done

- No storm porch: a door opens straight to the weather (a canopy and steps only). An inner lobby needs a wall piece that
  stands off the 2 m module.
- Board-marked concrete is the `concrete` finish itself since 2026-11-14 (a generated detail texture with tie holes, rain
  streaks and dirt at the foot: docs/render/MATERIALS.md "Detail textures"). Interior floors can take `terrazzo`, `lino` or
  `ceramic tiles`.
- The windows have no glass, as every building piece's.
