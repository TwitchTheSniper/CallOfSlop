# How a building should look

Read [look.md](look.md) first: paint, finishes, lamps and the detail parts are there. This file is the style guide for
**buildings**: what every building in the story's worlds needs, then each faction's style. A faction's buildings are
construct blueprints from the part kit (`Source/PartKit/Generators/BuildingParts*.cs`, `--gen-parts building_parts`); the
older map buildings (`Buildings/`) are flat-map things and don't stand on a planetoid.

The first style is the Ministry of Slop's, on Glacier (the user, 2026-11-12: "Brutalism, which will contrast well with the
cold. Old cold war kind of vibes inside; the machines are old, light switches etc are old. Modelled with the intention of
fully fitting their role, based on architectural knowledge"). The other factions have none yet: write theirs here, to the
same headings, before building for them.

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

## The Ministry of Slop: Brutalism

The Ministry builds as a government that expects to be there for ever and has stopped spending: heavy raw concrete,
honest about its weight, a little too big for what goes on inside, and unchanged since it was poured. Against Glacier's
snow it is a row of grey masses with amber lamps.

| | |
| --- | --- |
| **Material** | Concrete, bare. Walls `#a29e94`, everything that carries or caps (lids, columns, piers, canopies, steps, slabs) weathered `#858179`, finish `concrete`. Never corrugated tin, never wood outside. The infirmary alone is whitewashed (`#d6dad3`). |
| **Accents** | Ministry green `#2f5a3c` on doors and filing; navy `#1f2b45` signs; slate `#4b5157` on furniture and fuse boxes. |
| **Light** | Outside: `Bulkhead Light`, sodium amber `#ffb347`, under every canopy and overhang. Inside: `Strip Light`, a cold green-white `#e4f2dc`. Warm outside, cold inside: the wrong way round, as it would be. |
| **Roof** | Flat, always, with **the lid**: `Concrete Fascia` round every roof slab, 0.75 m deep and 0.5 m out, with its corners. A roof without its lid is not the Ministry's. |
| **Windows** | Few and never at eye level: `Concrete Ribbon Wall` (a high strip under a hood) where people work, `Concrete Slot Wall` (tall 0.25 m slots) where things are kept. A wall with nothing to light is blank. |
| **Weight** | `Concrete Buttress` (stepped piers) on blank and long walls, `Concrete Fin` on fronts, at 2 m centres (a wall's `f0_5`, `f8_5`). Upper floors may oversail the ground floor on `Concrete Column`s; a roof may be thrown out with nothing under it. |
| **The way in** | `Concrete Canopy 2 m` over a door corner's doorway with a lamp under it and the name on its front, or the door under an overhang; `Concrete Entrance Steps 2 m` in front. |
| **Signature** | Each building is one plain mass, or two of different heights, with one bold move: an overhang, a tall hall, a stack, a dock, a cantilever. |
| **Signs** | Navy `Text Sign`s in capitals, in the Ministry's voice: a name, then an instruction nobody could follow. |

### Inside: the cold war

Nothing inside is newer than the building. The fittings are the part kit's (`InteriorParts.ColdWar.cs`):

- **Bakelite Switch**: a round brown light switch, a `switch` logic part. Every room's strip lights are wired to one by
  the door (`"wires"`), started on, so a player can turn a room's lights off with it.
- **Strip Light**, **Bulkhead Light**: mains lamps (lit from the building's battery, the fuse box high on a wall).
- **Cast Iron Radiator** under windows and by desks; **Wall Clock** (stopped) where people wait.
- **Filing Cabinet** (storage, four drawers), **Teleprinter**, **Tape Cabinet** (the Ministry's computer: reels and
  lamps). These have a `"wall"` socket low on the back: hung on a wall's bottom face socket (`k8_0`) they stand on the
  floor against the wall, where a pad's own sockets (0, 1.75, 2 and 3.75 m) would leave them a metre out.
- With them, the older parts that fit: Mess Table (a counter), Locker, Parts Shelf, Medical Station, Bunk, Galley Stove,
  Tea Urn, Computer, Fax Machine, Phone Box, Pillar Box, Queue Post, Belisha Beacon, Employee of the Month.

Colours inside: cream, Ministry green, cast-iron grey, brown Bakelite; `worn paint` on everything painted.

### The street (`BuildingParts.Ministry.cs`)

| Building | Its job, and what that made it | Outline |
| --- | --- | --- |
| **Ministry Records Office** | Paper is kept dark, dry and above everyone: the archive is a blind box of slots, 8 by 12 m, over an 8 m hall, oversailing the forecourt on three columns so the queue stands under it. A counter splits the hall; the stair is behind it. Tape cabinets and filing upstairs. | The tallest mass, top-heavy |
| **Colony Canteen** | One room for everybody at once: a hall two storeys high with a clerestory ribbon right round (steam and noise go up, nobody sees out). Food comes through a servery from a low kitchen behind, which has its own back door for deliveries and the range's stack. | Tall hall, low block, the stack |
| **Colony Infirmary** | A ward: long and low. A blank buttressed wall to the weather, a high ribbon over the beds on the other. Triage at the door, the drugs and the files furthest from it. Whitewashed. | Long, low, white |
| **Requisitions Store** | A dock and a counter: the roof runs 4 m out over a dock at lorry-bed height; nobody passes the counter in the opening. Slots, not windows. | Deep roof on two columns |
| **Colony Gatehouse** | The clerk stays by the radiator: the roof is thrown 4 m towards the gate with nothing under it, and whoever is being checked stands there. | A post under a long lid |

`--look-report` shows these as almost wholly "dark or grey": that is the style, not a fault. Judge them by outline, lamps
and finish count, and in the window (`--window-shot OUT.png --blueprint "Colony Canteen" --at X,Y,Z --look-at X,Y,Z`).

### Building in the style

The helpers in `BuildingParts.Ministry.cs` do the repeated work: `Roof` and `Lid` (slabs and their fascias; `front:
false` where a taller part stands), `Porch` (canopy, lamp, name), `Steps`, `Piers` (fins or buttresses on a wall's face
sockets), `Outside` and `Strip` (lamps in the two colours), `Switch` (a Bakelite Switch wired to its lamps: a wire
reaches 8 m), `Sign`, `Door`. A blueprint holds up to 128 steps.

What the game's build check taught (the generator's mirror doesn't know what is in the way: run
`--check-construct-parts`, which builds every blueprint, after `--gen-parts building_parts` and a build):

- A canopy, a lamp or a sign on a wall's top row (`..._10`) is clear of the lid, which starts at the slab. A Text Sign
  on the top row hits a slab that runs out over it: put the name on the fascia's front (`f15`) there.
- A fin or a buttress takes its wall's three face sockets at that point; nothing else hangs there. Keep them off corner
  pieces (their plinth band would share a plane) and off a door's leg.
- Walls stand on walls for a tall hall (no floor between); a slab that meets a wall from outside at an upper storey's
  foot is why the corners' plinth band starts 4 mm up.
- The west wall upstairs over a `Concrete Stair Floor`'s opening (x 0.1 to 1.1 m, z 0.9 to 3.9 m) is the stairwell's.
- Furniture on the `"wall"` socket: two on neighbouring sockets (`k7_0`, `k8_0`) overlap; use `k0`, `k8`, `k15`.

### Not done

- The colony itself (the yard, the cell block, the warden's office) and the Colony Barracks, the Remote Works and the
  Sealed Data Centre are still plain concrete and tin rooms: the lore's items and the staff's posts stand at fixed spots
  in them, so they are restyled in place, keeping every room's floor, or those spots move with them.
- No storm porch: a door opens straight to the weather (a canopy and steps only). An inner lobby needs a wall piece that
  stands off the 2 m module.
- No board-marked finish: the walls use `concrete`. A `Materials/Surfaces.json` row with a plank pattern would do it.
- The windows have no glass, as every building piece's.
- The settlements of the Call Centre, the IT Department and Clan MacSlop share four unpainted blueprints: each needs its
  own style here, then its own buildings.
