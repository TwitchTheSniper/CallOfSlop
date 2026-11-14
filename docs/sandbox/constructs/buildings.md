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

## The site: what the buildings stand on and what joins them

A group of buildings is a place only when the ground under it was made for it and something runs from door to door. The
first site is the penal colony's (`Game/GameColonySite.cs` lays it, `Story/Opening/ColonyBuildings.json` is the plan,
`BuildingParts.Site.cs` the parts and blueprints). Plan a faction's settlement to the same headings.

1. **One level.** A camp on frozen ground is founded on a bench: the land is cut and filled to one height before anything
   is built. On a fresh story world the plan's `"bench"` rectangle (68 by 62 m in the colony's own frame) is graded to
   the colony's plane with ground ops (a player's Flatten, through `Game.Dig`: it saves and reaches a joiner as dug
   ground), a hand's breadth over the highest land so the whole bench is one surface: on Glacier, dark cinder with snow blown over it
   (the surface's `stone` and `dusting`, Maps/README.md). Each `"ramps"` entry is a slope from its edge to the land. Every building then stands at one height; no
   building is placed on raw land at its own level. Land more than `"most"` m off the colony's level is left alone and
   the site is not laid.
2. **Zones, as the job has them.** The yard is the secure compound (the only fence). The Ministry's street runs along
   its front: the public buildings (infirmary, records, canteen, stores) in one row facing the yard, the gatehouse at
   the gate. Staff live west of the yard (the barracks, its door to the muster square); work goes east (the Remote
   Works). Services stand at the downwind end by the road: the boiler house, behind a coal forecourt. The way in is one
   road, from the east, between two piers (rule 8).
3. **A drawn plan.** Rows marked `"site"` are laid at exactly their spot in the colony's plane, square with it, `"sink"`
   m into the bench (no search, no clearance check): the plan says where things are, so pieces meet edge to edge. Rows
   marked `"elsewhere"` fix the spot of a building another file builds (the barracks, the Remote Works).
   `[site]` lines in the log give every footprint in the plan's frame: lay a new piece against those numbers.
4. **Hard ground where people walk.** `Concrete Road Slab` (4 by 4 and 4 by 2 m, a footing, 0.12 m proud of the bench):
   the street (`Colony Road 24 m`), the muster square, the way to the gate. Columns, posts and a flag's base stand on a
   slab's top sockets (at 0, 1.75, 2 and 3.75 m, as a pad's).
5. **A covered way on the walk made every day.** `Colony Gate Path`: the 8 m from the yard's gate towards the street is
   under a flat roof on six columns with its lid and two lamps; the last 2 m is open for the crew bus.
6. **Services above ground, and shown.** Nothing is buried in frozen ground: the `Heating Main` (two lagged pipes on
   concrete trestles) runs from the boiler house behind the street's buildings and up the west side to the barracks, a
   `Heating Valve Chamber` where it ends or turns, and where it meets the road two `Heating Main Riser`s lift it over
   (`Heating Main Portal`, 3 m clear). The `Colony Boiler House` is a tall single hall for one `Lancashire Boiler`
   (furniture: it heats nothing) with the tallest stack in the colony (`Concrete Chimney 2.75 m`, four high).
7. **A place run by notice.** `Colony Gate Pier` (lamp, name, warning), the flag on the muster square, `Concrete Notice
   Board 2 m` with two notices each. Signs follow the Ministry's voice.

8. **One fence, and a reason for each way through it.** `Colony Fence` runs (precast `Concrete Fence Panel 4 m`, wire
   over it) stand 2 m inside the bench's edge, x -20 to 44 and z -36 to 20. Two ways through, both across the street's
   ends: the main gate in the east side between the piers, its `Sliding Gate Leaf` pair slid open along the inside of
   the fence (people, the staff's bots and the crew bus pass), and the service gate in the west side, shut, where the
   mains' portal stands over the road. A long thin body has a big radius, and the old ring search for clear ground
   (`SandboxKitPiece`) keeps that far from it: anything placed after a fence must be given its spot.
9. **A yard for what the player is given.** A survival world's starter machines and rigs stand in the works yard, the
   plan's `"kit"` rectangles inside the fence (`ColonySiteKit`: a 1.5 m grid, footprints clear of each other), not
   wherever the ring search finds room.
10. **Every building is on the heat.** A `Heating Branch` leaves the mains for each street building's back wall (2, 3,
    6 and 6.5 m: lengths of 4, 2, 1 and 0.5 m end to end; a branch stands between the mains' trestles, which are at
    every 4 m length's middle).

Not done on the site: the yard, the gatehouse and the Remote Works have no branch of the mains; the gates are furniture
(the main gate can't be shut, the service gate can't be opened); the street's slabs stop at x -16 and 32; a world from
before the site keeps its ground and gets none of it; the other factions' settlements have no site.

To see a site whole: `--window-shot OUT.png --survival --calm --map Glacier --do "key Jump; wait 40; from Flagpole Base
14,32,48; wait 30"` (`--calm`: no storm; `from PART side,up,out` puts the eye after the opening has woken the player).

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
To give another faction's place life, name its parts in the file: no code. People are not this file's business (the
story's people have their own posts).

Cost (measured 2026-11-13 in the agent container, the colony in view): the world's bodies are looked through twice a
second, the frames between walk a short list (seven parts in the colony): about 3 microseconds a frame plus 1 for the
searchlight, against 6.5 for the lamps' own code on the same machine; three rays every fifth of a second; no
allocation in a steady frame. Particles go into the one pool at the Particles setting's share.

Not done: a searchlight has no beam and no lit lamp head (only its pool on the ground); smoke rises along world +Y
(the particle pool's gravity), right at a pole and wrong far from it; no steam on the mains' pipes themselves; the
tannoy is one recording.

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
  lamps). These, the Locker and the Gun Rack have a `"wall"` socket low on the back: hung on a wall's bottom face socket (`k8_0`) they stand on the
  floor against the wall, where a pad's own sockets (0, 1.75, 2 and 3.75 m) would leave them a metre out.
- **Service Counter 2 m / 1.5 m** (a wooden counter, closed side to the public, a shelf on the staff's; lengths chain on
  `+x`/`-x`), **Steel Desk**, **Waiting Bench 2 m** (a seat), **Iron Bed** (a seat that is a Bunk: a body wakes there),
  **Store Shelving 2 m** (storage): all on the `"wall"` socket, so they stand against a wall.
- With them, the older parts that fit: Locker, Parts Shelf, Medical Station, Bunk, Galley Stove, Tea Urn, Computer, Fax
  Machine, Phone Box, Pillar Box, Queue Post, Belisha Beacon, Employee of the Month. A Mess Table is a canteen's table,
  not a counter.

Colours inside: cream, Ministry green, cast-iron grey, brown Bakelite; `worn paint` on everything painted.

### The street (`BuildingParts.Ministry.cs`)

| Building | Its job, and what that made it | Outline |
| --- | --- | --- |
| **Ministry Records Office** | Paper is kept dark, dry and above everyone: the archive is a blind box of slots, 8 by 12 m, over an 8 m hall, oversailing the forecourt on three columns so the queue stands under it. A counter splits the hall; the stair is behind it. Tape cabinets and filing upstairs. | The tallest mass, top-heavy |
| **Colony Canteen** | One room for everybody at once: a hall two storeys high with a clerestory ribbon right round (steam and noise go up, nobody sees out). Food comes through a servery from a low kitchen behind, which has its own back door for deliveries and the range's stack. | Tall hall, low block, the stack |
| **Colony Infirmary** | A ward: long and low. A blank buttressed wall to the weather, a high ribbon over the beds on the other. Triage at the door, the drugs and the files furthest from it. Whitewashed. | Long, low, white |
| **Requisitions Store** | A dock and a counter: the roof runs 4 m out over a dock at lorry-bed height; nobody passes the counter in the opening. Slots, not windows. | Deep roof on two columns |
| **Colony Gatehouse** | The clerk stays by the radiator: the roof is thrown 4 m towards the gate with nothing under it, and whoever is being checked stands there. | A post under a long lid |

The colony itself and the guards' quarters are in the same file:

| Building | Its job, and what that made it | Outline |
| --- | --- | --- |
| **Penal Colony** | A yard is for being counted in: wire a guard sees through, between four concrete posts with a floodlight each. The cell block is a blind box two storeys high with one window near the top and its light switch outside the door. The warden's office is low, has the Ministry's one window at eye level (he watches the gate from his desk) and throws its roof 4 m over the muster, in front of the cell door. | A tower and a long lid inside a fence |
| **Colony Barracks** | Guards sleep away from the door and leave by it fast: a narrow slab two storeys tall, the guardroom below (duty desk, gun rack, an open doorway with steps and a canopy), the dormitory of iron beds above under a high ribbon each side, the wide stair up the east wall from just inside the doorway. | A tall narrow slab, fins down its front |

The spaceport, 2 km out (`BuildingParts.Spaceport.cs`; docs/story/SPACEPORT.md):

| Building | Its job, and what that made it | Outline |
| --- | --- | --- |
| **Spaceport Terminal** | Arrivals and customs are one hall, split across the middle by the counter: landside the queue and benches, airside the departures desk and the cargo by the door to the apron. Its flat roof inside the lid is a landing deck, up a stair from the airside half. | Low and wide, a deck on top |
| **Spaceport Control Tower** | Controllers must see every pad and be seen in the weather: a blind shaft seven storeys high, slots up the stair, a glass cab on top (the one place the Ministry looks out), red lamps on the lid, a pad for small craft on the roof. A stair stacked storey on storey alternates sides (west flight, east flight), each under its own floor's opening, or the flight above leaves no headroom. | The tallest thing for miles |
| **Spaceport Landing Pad** | 16 by 16 m of foundation laid flush with the hardstand, a yellow touchdown square, a lamp on each corner. | Flat |

Each block has a number on its name board: 1 Canteen, 2 Records, 3 Infirmary, 4 Requisitions, 5 Barracks.

Each also has one thing on its roof nobody else has (`BuildingParts.Street.cs`), so the skyline tells them apart: the
Records Office the **Tannoy Mast** (the Ministry's voice), the Canteen a **Roof Sign Frame** with a slogan in cream on red
lit by two sodium lamps ("EAT WHAT YOU ARE GIVEN"; the warden's office has one too, to the gate), the Infirmary a **Roof
Water Tank** on legs, Requisitions a yellow **Loading Hoist** out over the dock from the lid, the Barracks an **Air Raid
Siren**. The `Slogan` helper puts up a frame, its board and its lamps. A roof piece stands on a slab's top socket at
`(row + 1) * C` (a slab's row is its centre: its top is half a cell up, the piece's foot half a cell more).

`--look-report` shows these as almost wholly "dark or grey": that is the style, not a fault. Judge them by outline, lamps
and finish count, and in the window (`--window-shot OUT.png --blueprint "Colony Canteen" --at X,Y,Z --look-at X,Y,Z`).

### Building in the style

The helpers in `BuildingParts.Ministry.cs` do the repeated work: `Roof` and `Lid` (slabs and their fascias; `front:
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
- A room that things are stood in at run time (the lore's items, Story/Lore: a wake spot, a post) keeps those spots
  clear: the build check can't see them, `--check-building` can (it stands the lore's items in and says when one is
  inside a part or out of reach). After a throwaway story world (`--traffic-check 5`) look for `[lore]` lines in the log.
- A 4 by 4 m room is four corners, which take no fin or buttress: give it height or a thrown roof instead. `Lid(...,
  back: false)` leaves the +z side off where a roof runs into a taller wall; close the rest by hand.
- A stair in a 4 m wide room: `Concrete Wide Stair Floor` turned `RY180` at `Cell(15, slab, z + 31)` with the
  `Concrete Wide Stair 3 m` `R0` at `Cell(8, G, z + 16)` puts it along the east wall, rising to the back (the Barracks);
  the floor `R0` at `Cell(0, slab, z)` with the stair `RY180` at `Cell(7, G, z + 15)` puts it along the west wall,
  rising to the front (the Records Office). Its head ends at the wall: a person steps off it sideways.

### Laying out a building

Karl's report (2026-11-14): the stairs had to be crouched and tables stood in the way. The rules that fixed it, which
`--check-building NAME[,NAME...]|all` checks (a standing body, 0.4 m round and 1.8 m tall, stepping up 0.55 m at most,
as the game walks it and as the colony's people path; `BuildingWalkCheck.cs`, maps of each level in `building-walk.txt`):

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
4. **Everything used is reachable**: every seat, store, switch and lore item within 1.6 m of somewhere a person can
   stand and leave from. A switch over a stairwell is out of reach; put it at the stair's head.
5. **A doorway opens onto floor**, not onto a flight's head: in a shaft of stacked flights the ground floor's goes up the
   side away from the door.

Run it after `--gen-parts building_parts` and a build, with `--check-construct-parts`. It changes nothing; the exit code
is the number of buildings with a failure.

### Signs

A penal colony's signs are signwritten boards, not screens (the other factions keep their lit Text Signs): the part
kit's `Sign Board 3 m`, `Sign Board 2 m` and `Notice Board` (`ControlParts.cs`), whose painted colour is the board and
whose words are drawn in cream on a dark board, black on a light one, shrunk to fit and wrapped on the notice. In the
generator: `NameBoard` (cream on Ministry green, "BLOCK 4 - REQUISITIONS", 2 m on a canopy), `Notice` and `Sign` (black
on cream, three lines of rules), `Warning` (cream on red, one line: "STAFF ONLY"). Every block has a number; every
notice is a rule nobody can keep.

## Out on the ice: the factions' outposts (`BuildingParts.Outposts.cs`)

Each faction keeps a small outpost beyond the colony (docs/story/OUTPOSTS.md). The Ministry's two are in its style above;
the other three factions' styles start here, at outpost size: one room, and the thing outside that says whose it is.

| | Clan MacSlop: drystone and tin | The IT Department: beige steel | The Call Centre: portacabins |
| --- | --- | --- | --- |
| **Material** | Walls and pad stone `#8a8072`, finish `stone`; never concrete-grey | Steel walls `#c8c2ae` (every computer it ever owned), `painted steel` | Corrugated walls `#7a2f6e`, `gloss paint` |
| **Roof** | A tin ridge, `#6e3324`, `rusty paint` | Flat, a grey steel lid `#5a5f63` | A single tin slope in the walls' purple |
| **Accents** | Saltire blue `#1f4f9a` signs; dark timber doors `#3b2a1c` | Teal `#1c8c99` door and signs | Gift-card gold `#e0b030` door and signs |
| **Light** | Firelight `#ff8a3c` at the door | Cold blue-white `#cfe8ff` | A pink nobody asked for, `#ffd6ef` |
| **Windows** | One small one, away from the weather | None: nothing inside wants to look out | One on the road, to watch who walks past |
| **Signature** | Outside, not in: the saltire on its pole, casks, a keg, the pipe box, sandbags half across the front towards the Ministry | Masts and dishes you see from a kilometre, a drum of cable nobody rewound | The phone mast, a loudhailer facing the colony, a prize wheel that never paid out |

| Building | Its job, and what that made it | Outline |
| --- | --- | --- |
| **Ministry Checkpoint** | Stopping people: a 4 m road between the booth (door and window on the road) and a barrier line that closes the forecourt either side of it. The flag on its tall pole over the barrier is lit, to be seen in the snow. Queue posts where nobody will ever queue. | A low box, a line, a tall pole |
| **Ministry Sentry Post** | Two men on foot need a hard place to stand: a pillbox, sandbags across half the forecourt, the patrol's way out through the other half, the flag to walk back to. | A squat block behind a low wall |
| **Clan MacSlop Bothy** | A shelter for whoever is passing: one stone room under a tin ridge, a door that doesn't lock, a bunk. The clan's own touches are all outside. | A cottage, a flag, casks |
| **IT Field Cabinet** | Keeping a rack dry and running, and being found by the engineer sent to reboot it: a blind steel box with the rack in the middle, the mast and the dish on the pad beside it. | A box and a mast |
| **Call Centre Relay** | Being heard: the mast is the tall thing, the loudhailer faces the colony, the cabin watches the road. | A cabin and a mast, loud |

What stands over each outpost is what reads at 50 m in the snow, and the dressing parts (a phone mast, a dish, a prize
wheel) are a vehicle's size, under a metre: so each outpost has a structure of its own on its pad. `LatticeMast` stands
lengths of medium truss on a pad (through an `Adapter Medium to Small`: a pad's sockets are small) with a floodlight on
top; `TallPole` is a flagpole of several masts. Checkpoint Four has a floodlight mast over the barrier and a two-length
flagpole; Sentry Post Nine a searchlight tower over the forecourt; the Bothy a stone stack against its gable that
smokes; the IT cabinet a 12 m mast with a cold light; the Relay a 16 m white mast with the pink light, the tallest thing
on the ice. The Ministry Quarry Works (a hand-made blueprint) has the same floodlit mast over its face.

### Not done

- The Remote Works and the Sealed Data Centre are still plain concrete rooms (`BuildingParts.cs`). The game finds the
  Works' chairs and apron by their places (`DepotSpots` in GameDepot.cs), so it is restyled in place. The Data Centre
  is from before the Ministry: it wants its own, older style, not this one.
- The site (level ground, paving, what joins the buildings) is another piece of work: see "The site" when it lands.
- No storm porch: a door opens straight to the weather (a canopy and steps only). An inner lobby needs a wall piece that
  stands off the 2 m module.
- Board-marked concrete is the `concrete` finish itself since 2026-11-14 (a generated detail texture with tie holes, rain
  streaks and dirt at the foot: docs/render/MATERIALS.md "Detail textures"). Interior floors can take `terrazzo`, `lino` or
  `ceramic tiles`.
- The windows have no glass, as every building piece's.
- The settlements of the Call Centre, the IT Department and Clan MacSlop share four unpainted blueprints: their styles are
  above now ("Out on the ice"); their homes still want buildings in them.
