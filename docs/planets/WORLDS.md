# The story's worlds

Five star systems, one faction home in each (Karl, 2026-11-10; the storyline has five systems that each solved a part
of the fold, and a data centre in each). A world is a planetoid map in `Maps/Planetoids/`, named by a body's `"map"` in
its system file. Every map there is written by `tools/planets/make_worlds.py`: change a world in the script and run it,
then look at it with `--check-planetoid` (docs/planets/PLANETS.md "Making a world").

| System | World | Radius, gravity | Ground | Faction home | Why go |
| --- | --- | --- | --- | --- | --- |
| Dev (Slopstar) | Planetoid (`Maps/Dev`) | 10 km, 18 | temperate hills, two mountains | Ministry of Slop: the penal colony | where the story starts; data centre 1 |
| Dev | Bluestone (`Maps/Dev`) | 3 km, 9 | blue-grey, one big mountain | none | the first trip out |
| Dev | Redrock | 4 km, 9 | red desert: mesa country in the north, the Gash (a 5 km canyon), the Steps, a dune sea | a Ministry mining concession | a practice landing before the first jump |
| Ember | Cinder | 5 km, 12 | black basalt and craters, lava-coloured low ground, long fissures; one face always to its red sun | the Call Centre: the Call Floor | data centre on the night side |
| Frostreach | Glacier | 8 km, 14 | snowfields, blue ice ridges, four nunataks, the Rent (a 9 km crevasse), the Mirror (a frozen lake), the Shelves | the IT Department: the Server Farm | data centre at the bottom of the Rent |
| Glenmore | Caledon | 6 km, 11 | bens with snow on top, long glens, heather slopes, bog | Clan MacSlop: the Glen | data centre in a corrie under Ben Slop |
| Hearth | Mille | 3 km, 8 | hollow: a golden crust stepped in layers, 220 m thick, round a glowing cavity | the Thousand Layers: the Bakehouse | data centre inside the cavity |

## Places kept flat

Each world has three level places, flattened after everything else, so later work (a settlement's blueprint, a data
centre) has ground to stand on. Latitude 90 is the +Y pole and longitude 0 faces +Z (PLANETS.md). The home and the
data centre's site each have a 4 m survey post (yellow for the home, blue for the data centre) and nothing else.

| World | Landing ground (spawns) | Faction home | Data centre's site |
| --- | --- | --- | --- |
| Redrock | the pole, 70 m, height 14 | 55, 60: a mesa top, 90 m | 8, 190: a ledge in the Gash, 70 m, 236 m down |
| Cinder | the pole (on the twilight line), height 10 | 12, 130: the Call Floor, a crater's floor 420 m across, 150 m down | -10, 50: the floor of a deep crater on the night side, 70 m |
| Glacier | the pole, height 20 | -10, 320: the Mirror, 700 m of flat ice | 15, 170: the bottom of the Rent, 80 m, 318 m down |
| Caledon | the pole, height 30 | 30, 150: the Glen's floor, 160 m | 60, 152: a shelf at 640 m under Ben Slop, 70 m |
| Mille | the pole, height 9 | 55, 300: a terrace, 90 m | 70, 40: the Proving Bowl, where the crust is thinnest (about 150 m): the site itself is inside, under the bowl |

Cinder turns once an orbit, so its sun stays over latitude 0, longitude 220 (`--check-planetoid` prints it): the day
side is centred there, the night side on longitude 40, and the twilight line runs through both poles and longitudes
130 and 310. Change the body's rotation `"angle"` and those move.

## Getting there

A jump arrives in orbit round the system's main world (`"arrive": { "at": "Cinder", "km": 30 }` in its system file,
docs/space/GALAXY.md): 40 km from the Planetoid, 30 from Cinder, 45 from Glacier, 35 from Caledon, 20 from Mille. Fly
down into the world's sky and the whole group lands there, with everyone seated aboard (docs/space/SPACE.md "Landing
on another planetoid"): the bubble moves to that system, the world left behind is kept, and every system's ships fly on.
Redrock is 1.5 AU from the home star's Planetoid, a long flight with no time warp: it is easier played by starting on it.

## Landing limits

Some worlds won't take a capital ship (Karl, 2026-11-10): it waits in orbit and a shuttle goes down. A world's map sets
the heaviest ship that may come down through its sky, counted with what it carries:

```json
"planetoid": { "landing": { "tonnes": 40, "why": "the crust is thin over the cavity" } }
```

| World | Limit | Why |
| --- | --- | --- |
| Cinder | 60 t | the ash flats swallow anything heavier |
| Mille | 40 t | the crust is thin over the cavity |

The group still arrives: the bubble moves to the world, and the heavy ship is held at the sky's edge as a ship in space
(`Game.LandOverLimit`, `LandHoldOut`), its riders told why every few seconds. A craft it carries in a cargo volume can be
let go there and flown down, since it is inside the sky's edge the moment it is free. The limit is the sky's rule, not the
arrival's: a ship over it that was built on the world and flown out can't come back down either. In
`tools/planets/make_worlds.py` it is a world's `"limit": (tonnes, why)`; `--check-planetoid` prints it.

## Not yet

- **Free travel between systems.** A ship that has jumped lands with the whole group; a planetoid running beside the
  host's game (docs/space/FREE-TRAVEL.md) is opened only for worlds of the bubble's own system.
- **Docking in orbit** hasn't been tried with a shuttle coming back up to a ship held at the edge.
- **Ore.** Deposits are generated on flat maps only (`"resources": { "generate": false }` on every planetoid).
- **Settlements.** The homes are bare ground: each faction's place is a blueprint still to be made and put there.
- The moons and small bodies (Moonlet, Ashfall, Soot, Rime, Shard, Cairn, Skerry, Crumb, Proof) are rails and sky.
- Colours are one set of rules per world (height and slope), so Cinder's day side is not a different colour from its night.
