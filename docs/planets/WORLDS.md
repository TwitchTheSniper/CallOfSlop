# The worlds

Five planets in one star system, Frostreach (Karl, 2026-11-15; the storyline's five systems, one faction home in each,
went with it: Cinder, Caledon and Mille moved into Frostreach, docs/space/GALAXY.md). The Dev system stays for tests. A world is a planetoid map in `Maps/Planetoids/`, named by a body's `"map"` in
its system file. Together they are the game's map: the starts wake over or on a world
(docs/story/STARTS.md), and one saved world holds every world the group lands on. Every map there is written by `tools/planets/make_worlds.py`: change a world in the script and run it,
then look at it with `--check-planetoid` (docs/planets/PLANETS.md "Making a world").

**Four times the size (Karl, 2026-11-19).** The Frostreach worlds are written at their old size in the script and grown
there (`GROW`, `grown`): each is the same planet magnified, its big shapes (mountains, craters, rifts, ridges, broad noise,
the regions of terraces, crevasse and drift fields) four times wider and twice as tall, so slopes are half as steep, while
what is underfoot (fine noise, crevasses, drifts, caves) and each site's own flat ground keep their metres. The snow and
sand lines, the clouds and the sky's edge (5.2 km up, not 2.6) grow with the heights. Mille grows by 4/3 only (a hollow
world is at most 4 km), Redrock (the Dev system's) not at all. Surface gravity is unchanged, so orbits go twice as fast;
the moons' orbits in `Systems/Frostreach.json` are four times as far. Past 16 km a float steps by 2-4 mm, so the physics
keeps what floats can't hold (PLANETS.md "Origin").

| System | World | Radius, gravity | Ground | Kept site | Why go |
| --- | --- | --- | --- | --- | --- |
| Dev (Slopstar) | Planetoid (`Maps/Dev`) | 10 km, 18 | temperate hills, two mountains | none | tests |
| Dev | Bluestone (`Maps/Dev`) | 3 km, 9 | blue-grey, one big mountain | none | the first trip out |
| Dev | Redrock | 4 km, 9 | red desert: mesa country in the north, the Gash (a 5 km canyon), the Steps, a dune sea | a mesa top | a practice landing |
| Frostreach | Cinder | 20 km, 12 | black basalt and craters, lava-coloured low ground, long fissures; one face always to its red sun | a crater's floor | ore; a deep crater on the night side |
| Frostreach | Glacier | 32 km, 14 | snowfields, blue ice ridges, four nunataks, the Rent (a 36 km crevasse), the Mirror (a frozen lake), the Shelves | the Mirror | the bottom of the Rent |
| Frostreach | Caledon | 24 km, 11 | bens with snow on top, long glens, heather slopes, bog | the Glen's floor | a shelf under Ben Slop |
| Frostreach | Mille | 4 km, 8 | hollow: a golden crust stepped in layers, 254 m thick, round a glowing cavity | a terrace | inside the cavity |
| Frostreach (moonlet of Mille) | Crumb | 2.4 km, 1.5 | a grey lump of rubble pocked with craters; the sky's edge only 2.8 km up (`"sky"` in make_worlds.py) | none | the Magpie start wakes over it (docs/story/STARTS.md): iron, stone and sand, the ores a tier 1 drill works |
| Frostreach (moon of Glacier) | Slopolis | 14 km, 10 | one city over the north (docs/planets/CITY.md), hills beyond | none: the free port | towers, streets, the elevated loop |

## Places kept flat

Each world has three level places, flattened after everything else, so later work (a settlement, a station's ground
base) has ground to stand on. They were the old factions' homes and the story's data centres, which went with the story
(2026-11-21, docs/removals-notes.md). Latitude 90 is the +Y pole and longitude 0 faces +Z (PLANETS.md). The first and
second site each have a 4 m survey post (yellow and blue) and nothing else.

| World | Landing ground (spawns) | First site | Second site |
| --- | --- | --- | --- |
| Redrock | the pole, 70 m, height 14 | 55, 60: a mesa top, 90 m | 8, 190: a ledge in the Gash, 70 m, 236 m down |
| Cinder | the pole (on the twilight line), height 10 | 12, 130: a crater's floor 420 m across, 150 m down | -10, 50: the floor of a deep crater on the night side, 70 m |
| Glacier | the pole, height 20 | -10, 320: the Mirror, 700 m of flat ice | 15, 170: the bottom of the Rent, 80 m, 318 m down |
| Caledon | the pole, height 30 | 30, 150: a glen's floor, 160 m | 60, 152: a shelf at 640 m under Ben Slop, 70 m |
| Mille | the pole, height 9 | 55, 300: a terrace, 90 m | 70, 40: the Proving Bowl, where the crust is thinnest (about 150 m): the site itself is inside, under the bowl |

Glacier has a cold storm outdoors (`"storm": true`, set in make_worlds.py): driven snow, a whiteout down to about
45 m at its height, frost and wind, gone under a roof and above 700 m (Maps/README.md "Storm"). Under it is a polar
dusk (`"dusk": 0.85`): dark and blue, with lamps throwing orange pools. Made ground on Glacier is dark
cinder with snow blown over it (the surface's `stone` and `dusting`), so a levelled bench isn't the blue of the ice.

### Glacier's ground

Glacier was mostly gentle snow: walking out from the pole there was nothing to steer by for kilometres. Its rough
country (made in make_worlds.py's `glacier_terrain`, Karl 2026-11-13: "real terrain") is in two parts:

- **Near the pole**, each a short walk out and none reaching within 700 m of the pole: **the Spine**, a ridge 110 m
  high and 2.6 km long about 1.2 km out along longitude 285 (west of the pole), with passes in its crest and an **ice
  cave** opening on its pole side (82.21, 282); **the Rucks**, a field of pressure ridges 5 to 9 m high about 1.5 km
  out along longitude 210; **the Teeth**, a crevasse field 900 m across about 1.4 km out along longitude 150, cracks up
  to 24 m deep with routes between them.
- **Over the whole world**, by a seeded draw that keeps clear of the pole, the old spaceport's ground and both sites: nine more
  ice ridges (50 to 150 m) with a cave under each of the six tallest, twelve crevasse fields, seven pressure-ridge
  fields, and sastrugi (wind drifts half a metre high) on all the open snow from about 830 m out from the pole.
  Patches of bare blue ice (the surface's `ice`, `icePatches`, `iceScale`: colour only) lie on gentle ground everywhere.

Kept as it was: the ground within 700 m of the pole (where the penal colony stood), the old spaceport's ground about 2 km out along longitude 90 (only the sastrugi reach it: it grades a
bench anyway) and the two sites (flattened last). A world saved before this keeps its builds there; one standing out
on the open snow may find the ground a quarter of a metre higher or lower, and one where a ridge or a crack now is will
float or sink. Caves are dug only into fresh worlds.

### Settlements

No faction has a settlement on a world yet: the old factions' settlements (the Call Floor, the Server Farm, the Glen)
went with the story (2026-11-21, docs/removals-notes.md). The sites above keep their flat ground for the next ones.

Cinder turns once an orbit, so its sun stays over latitude 0, longitude 220 (`--check-planetoid` prints it): the day
side is centred there, the night side on longitude 40, and the twilight line runs through both poles and longitudes
130 and 310. Change the body's rotation `"angle"` and those move.

## Getting there

A jump arrives in orbit round the system's main world (`"arrive": { "at": "Cinder", "km": 30 }` in its system file,
docs/space/GALAXY.md): 40 km from the Planetoid, 30 from Cinder, 45 from Glacier, 35 from Caledon, 20 from Mille. Fly
down into the world's sky and the whole group lands there, with everyone seated aboard (docs/space/SPACE.md "Landing
on another planetoid"): the bubble moves to that system, the world left behind is kept, and every system's ships fly on.
Redrock is 1.5 AU from the home star's Planetoid, a long flight (a coast of weeks, sped up from the command view): it is easier played by starting on it.

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

## Ore

Every planetoid has ore now (the three Dev ones too). A round world has no (x, z), so its deposits lie on the six faces of
the cube round it (`Source/Constructs/ResourceSphere.cs`): a point's direction from the centre picks the face and two
angles across it, in metres along the ground, and each face is a field generated like a flat map's. Deposits stop at a
face's edge. A Drill Module taps what is under its bit and digs its hole with down towards the centre
(`ConstructGatherer.WorkRound`, `DrillShaft`'s planetoid half); a deposit shows as a streak of rocks on the ground within
400 m, and looking at a drill names the nearest deposit and the way to it (north is towards the +Y pole).

Each world has its own mix: `"sandbox": { "resources": { "mix": { "gold vein": 2, "sand bed": 0 } } }` multiplies a
kind's chance from `Constructs/Resources.json` (0 to 4; a kind not named keeps its own). In `make_worlds.py` it is a
world's `"ore"`. `--check-planetoid` prints the count of each kind and the share of ground over a deposit.

| World | Commoner | Rarer or none |
| --- | --- | --- |
| Redrock | iron, sand | gold, helium |
| Cinder | copper, gold, stone | sand; no helium |
| Glacier | helium, copper | iron; no sand |
| Caledon | stone, iron | sand |
| Mille | levite, gold, sand | iron |
| Crumb | iron, stone, sand (31% of its ground is over a deposit) | no copper, gold, helium or levite |

Not on a planetoid yet: forests (trees stand along +Y), deposits a map places by hand, and a hole dug from inside a hollow
world's cavity (a drill on the cavity's wall taps but digs nothing).

## Not yet

- **Free travel between systems.** A ship that has jumped lands with the whole group; a planetoid running beside the
  host's game (docs/space/FREE-TRAVEL.md) is opened only for worlds of the bubble's own system.
- **Docking in orbit** hasn't been tried with a shuttle coming back up to a ship held at the edge.
- **Settlements.** The homes are bare ground: each faction's place is a blueprint still to be made and put there.
- The moons and small bodies (Moonlet, Ashfall, Soot, Rime, Shard, Cairn, Skerry, Proof) are rails and sky; Crumb is the one with a map.
- Colours are one set of rules per world (height and slope, and patches of a second colour), so Cinder's day side is not a different colour from its night.
- Only Glacier has the rough country (ridges, crevasses, drifts, caves); the other worlds can take the same features.
