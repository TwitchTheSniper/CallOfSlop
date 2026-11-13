# Cities on planetoids: Slopolis

Karl, 2026-11-13: a planetoid that is dense and full of life, with places, travel and ways to get about, and that
doesn't look blocky. The first is **Slopolis**, the free port: a 3.5 km world whose north is one city, built by
everybody and owned by nobody (`Maps/Planetoids/Slopolis.json`, made by `tools/planets/make_worlds.py`). It is a moon of
Glacier in Frostreach (`Systems/Frostreach.json`, 140 km out, a 3 h orbit), the story's home system, so a ship from the
colony can fly there; or start a Sandbox on it. Its axis stands upright to its orbit, so the city at the north pole is in
a blue hour that never ends (the sun about a degree up, going round the horizon once in its 26 h day). It has no ore: a
free port's stock comes in by ship (and a deposit's rocks would stand in the streets).

## How a city is made

A planetoid map's top-level `"city"` block (`CitySpec`, `Source/Planets/City/CityPlan.cs`) is worked out into a plan on
every PC alike: nothing is sent or saved, so a city costs no bandwidth, no save space and nothing on join.

| Key | Default | Meaning |
| --- | --- | --- |
| `lat`, `lon` | 90, 0 | The centre (the square). |
| `radius` | 600 | Blocks whose middle is inside are built (50 to 3000 m). |
| `height` | 0 | The ground's height there: the world's flatten must make it level (make_worlds.py does). |
| `seed` | 1 | Everything else is drawn from it. |
| `blockX`, `blockZ` | 70, 96 | Block sizes between building lines, jittered 15% each. |
| `street`, `avenue`, `avenueEvery` | 16, 26, 3 | Street widths (building line to building line) and how often an avenue runs. |
| `tallest`, `lowest`, `coreFalloff` | 240, 12, radius | Heights: the tallest in the middle, falling to the lowest by `coreFalloff` m out. |
| `loop` | 0 | The elevated railway's ring (m): it runs over the avenues nearest that ring. |

The plan (`CityPlan`): street lines through the centre and out both ways; blocks between them; the four blocks round
the centre are **the square** (no traffic: paving, trees, benches, kiosks, a column at the pole); every other block is cut
into lots, a building on each. Over 70 m a building is a **tower** (a podium of shops, a shaft stepping in once to three
times, a crown, an aerial on a few of the tallest; square-ish ones may be round or cut-cornered); under it a **mid-rise**
(straight up to a cornice, or a setback storey). Roofs carry parapets, plant rooms and water tanks.

The plan is laid on the sphere by true distance and bearing from the centre (`CityPlan.DirOf`); each building stands
upright in its own frame at its middle (`FrameOf`), so on a small world the towers lean apart a little, as they would.

## Drawing it (`Source/Render/CityRenderer.cs`)

`CityGeometry` turns the plan into chunks (128 m of the plan each): its triangles go to the GPU once, and a frame culls
the chunks by sphere and draws each visible one in one call, nearest first; rooftop clutter, lamps and cornices only
within 420 m. **The look is in the shader, not the geometry**: each face carries its own metres (along and up a facade,
along and across a street) and a style, and the windows, floors, shopfronts and their lit fascias, lit rooms (blinds,
curtains, desks), reflections of the sky, road markings, zebra crossings, the pools under the lamps, track and stairs are
worked out per pixel. Far off a floor shrinks under two pixels and the facade fades to its average (lit share kept), so
nothing shimmers. Lit things mark the bloom.

Cost on Karl's PC (RTX 4070 Ti, 1920x1080, vsync off, 2026-11-13): the whole city from above, 90 of 102 chunks, 165k
triangles: **0.04 ms CPU and 0.08 to 0.09 ms GPU a frame**; at street level 0.02 ms CPU, 0.15 ms GPU; the game ran at
1,800 to 2,800 fps in both views. Making the plan and its meshes takes 0.6 to 0.9 s when the map is first played (the
log's `[city]` line), 25 MB on the GPU. A map without a city pays nothing.

## Solid

Every building mass, pavement, block, the square's furniture, lamp posts, the railway's deck, columns, platforms and
stairs are closed triangle meshes added to the map's solid colliders (`Map.SolidColliders`, one collider a chunk, about
310k triangles), so players stand on pavements and platforms, climb the stations' stairs and can't walk through walls.
The carriageway is the ground itself.

## Getting about: what there is

- **Streets and avenues**: kerbs, pavements, lamps both sides every ~24 m, crossings with zebras.
- **The elevated railway**: a loop over four avenues (2.4 km), a concrete box girder on steel columns, two tracks, four
  stations (side platforms, canopies with lamps, stairs down to the pavement at each end).

## The metro (`CityMetro`, `GameCityMetro.cs`)

A line under the central avenue (`"metro": 1`, `"metroDepth"`, default 10 m): a station under the square, one under
each crossing of the elevated loop (interchanges: the el's stairs come down a few metres from the metro's), and one more
beyond them each way where a street crosses inside the city (four on Slopolis now). Each station is a hall 90 by 25 m:
two side platforms with benches and a yellow edge, tracks between, tiled walls with the line's blue band and a name
frieze, a suspended ceiling with strip lights, and a tunnel mouth at each end with 30 m of dark tunnel behind it. Four
flights of stairs come down from the pavements (an opening with railings and a blue lamp post on the street, a lined
shaft under it); the pavement, the street lamps and the walkers keep clear of the openings.

**Dug at a fresh world's start**, like the caves (`Game.CityMetroDig`, after `SandboxCaves`): the halls, the stubs and the
shafts as ground drill scoops (1,224 ops, about 0.1 to 0.3 s), so they save and reach a joiner as dug ground. A world
saved before the metro keeps its ground: its stairs lead down into rock.

**Its trains** (silver and blue, three cars, two trains) run from one end of the line to the other, stopping 24 s at
each station, turn back in the tunnel out of sight and come back on the other track: a train each way every few minutes
(`CityLife.MetroTrain`). People come down the stairs and wait, board, and get off and go up to the street, as at the el's
stations. Under the city's ground the city's and the figures' shaders light with the strip lights, not the sun.

## Places, signs and the sky (slice 3)

- **The market** (`CityGeometry.Market`): the block over the avenue from the square's north-east corner is a covered
  market: a steel roof on columns over rows of stalls (counter, shelf, striped awning, a lamp, wares), aisles between
  them, open all round, a lit sign over the side facing the square; shoppers go round between two aisles each.
- **Signs**: most buildings with shops have a lit blade sign (or two) standing out over the pavement near a corner, its
  letters flickering and now and then stuttering; one mid-rise in five has a lit billboard on its roof facing its street.
- **Rooftop pads**: 30 towers carry a landing pad on their crown (a dark deck with its circle and H, green lamps
  blinking round the rim). **Air taxis** (yellow, lit windows, nav lights and a beacon) hop between them: stand, climb,
  cross high over the towers, come down (`CityLife.Taxi`, from the clock like everything else).
- **The apron** (`CityGeometry.Apron`): a disc of rough asphalt just over the ground under the whole city, drawn first
  and pushed back in depth; the planetoid's own ground under it is not drawn (`PlanetRenderer.CityClip`). Without it the
  ground (and anything under 15 cm apart) fought the paving at a distance and showed through in dark patches.

Cost on Karl's PC with all of it (1920x1080, vsync off, 2026-11-13): 0.07 to 0.09 ms CPU and 0.09 to 0.12 ms GPU a
frame for the city; the game at 2,100 to 3,000 fps from above, in the market and in the avenue.

## Words, the square, traffic (slice 4)

- **Signs say something** (`CitySigns`): the panels' font (`PanelFontData`, a distance field) and a small table of words
  go to the GPU once; the shader writes a shop's name on its fascia (50 names, picked by the shop's seed), a blade's
  name down its face (letter by letter), a slogan over a billboard's picture, MARKET over the market, each metro
  station's name along its frieze (`CitySigns.Metro`, south to north: CENTRAL under the square) and each el station's
  on a board under its canopy. Faces are laid so a word reads the right way round from outside (`Box`, `Wall`).
- **Shopfronts**: nine in ten shops are open; a shut one has its roller shutter down; every shop has a framed glass door
  at one end of its window.
- **The square**: a fountain round the column (a stone basin, rippling water), four planted beds with trees, a ring of
  lamps across the open paving, darker stone in rings and spokes round the fountain.
- **The market's roof**: a sawtooth of north lights over its ceiling, the glazing lit from inside.
- **Traffic**: about 1,600 cars drive the streets' lanes both ways (two lanes each way on the avenues), keeping right,
  with head and tail lamps; drawn like the walkers, one instanced figure posed on the GPU from its lane and the clock
  (`CityLife.StreetCars`, kind 2), within 400 m. A car that reaches its run's end (the city's edge, the square) comes
  in again at its start. They are dressing, not vehicles: the vehicle system is not touched.

Cost on Karl's PC with all of it (1920x1080, vsync off): 0.07 to 0.09 ms CPU, 0.12 to 0.13 ms GPU a frame for the city;
the game at 2,670 to 2,840 fps from above, by the market and in the avenue.

## The square, the ways in, the skyline (slice 5)

- **The square**: twelve benches round the fountain facing the water, a ring of sixteen trees at 30 m, four cafe
  terraces (six tables under umbrellas, stools, a kiosk) between the beds; strollers keep clear of all of it.
- **Every way in is marked** (buildings.md rule 4): a canopy over the door in the middle of each front with a lamp under
  it; a lobby without shops has a deeper coloured canopy on two posts (`Entrance`).
- **The skyline**: a mass may taper (`Shape` 3, its top the outline scaled by `Cut`: hipped roofs, pyramids). Towers end
  four ways: the flat plant room (and maybe a pad), a stepped crown, a dark pyramid with a lamp at its point, or a lit
  crown band; half the brick blocks wear a hipped slate roof.
- **Cars**: a body, bonnet and boot, a dark glasshouse and a roof, wheels outside the body; a fifth are vans (longer,
  taller) or buses.

Cost (same PC and settings): 0.08 to 0.1 ms CPU, 0.13 ms GPU a frame for the city; 1,770 to 2,660 fps.

## Damage: the ground (phase 1)

Karl, 2026-11-13: make Slopolis destructible, the planet's ground too. Phase 1 is the ground.

- **The planetoid's own ground is the city's floor.** The city's flatten stands level with the pavements' tops
  (`CityGeometry.GroundTop`, 0.18 m over the city's ground radius; make_worlds.py), and the paving, the blocks' courts
  and the square have no colliders of their own any more: people, parts and wrecks stand on the ground itself, and a
  blast, a drill or a shovel digs it like any other world's (docs/sandbox/TERRAIN-DIGGING.md). The carriageways are drawn
  a kerb's height under it, the ground itself isn't drawn under the city (`PlanetRenderer.CityClip`), so the streets look
  as before.
- **The paving breaks where the ground is dug.** `CityRenderer.Damage` keeps a mask over the plan (1 m a texel) of where
  the dig log's craters and scoops cut the ground's top, adding only the ops logged since it last looked (a version
  check a frame while nothing is dug); the shader leaves the city's ground faces (carriageway, crossings, pavements,
  kerbs, courts, the apron) out over a dug texel, so the dug ground's own blocks show. The dig log is what saves and
  reaches a joiner, so a crater in the street is there for everyone and after a load; nothing new on the wire or in a
  save.
- Not yet (phase 2): buildings, lamps, the el and the furniture take no damage; a lamp post over a crater stands in the
  air; ground put back by a fill doesn't bring the paving back until the world is loaded again.

## The plan for the rest (slices)

1. **Done: the core** (this file): streets, 790 buildings, the square, lamps, the elevated loop and its stations.
2. **Trains and a metro, with people riding** (Karl, 2026-11-13: "a railway and metro... with stations that make
   sense, NPCs travelling around on both living their lives"): trains running the loop and stopping at stations (posed
   from the clock on every PC, like the stand-ins and the convict gangs: nothing streamed); an underground metro with
   entrances on the pavements, stairs down to a ticket hall and platforms; people on the pavements, waiting on
   platforms, getting on and off, each with somewhere to go (home, work, a shop) on a daily round. Trains are pictures
   with riders, not vehicles (the vehicle system is TwitchTheSniper's).
3. **Partly done (above): the market, signs, pads and taxis.** Still to come: more (a station hall, a market, a bar, a hotel lobby with lifts to a
   roof), signs and neon, rooftop pads where the stand-in flyers land, the docks at the edge.
4. **Getting there**: done, a moon of Glacier. Still to come: a spaceport where ships can land, traffic between it and
   the colony, the land beyond the last street (quarries, a road out).

## Not yet

- The city is not on the wire's map fingerprint: a host and a client on different builds could disagree about walls.
- Nothing of it can be dug, built onto by sockets or destroyed: it is scenery that collides.
- Bots have no nav graph on it (planetoids have none).
- The square's trees and the columns are simple low-poly shapes; no foliage system on planetoids yet.
