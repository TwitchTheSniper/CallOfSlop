# Cities on planetoids: Slopolis

Karl, 2026-11-13: a planetoid that is dense and full of life, with places, travel and ways to get about, and that
doesn't look blocky. The first is **Slopolis**, the free port: a 3.5 km world whose north is one city, built by
everybody and owned by nobody (`Maps/Planetoids/Slopolis.json`, made by `tools/planets/make_worlds.py`). It is in no
star system yet: start a Sandbox on it to visit.

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

## The plan for the rest (slices)

1. **Done: the core** (this file): streets, 790 buildings, the square, lamps, the elevated loop and its stations.
2. **Trains and a metro, with people riding** (Karl, 2026-11-13: "a railway and metro... with stations that make
   sense, NPCs travelling around on both living their lives"): trains running the loop and stopping at stations (posed
   from the clock on every PC, like the stand-ins and the convict gangs: nothing streamed); an underground metro with
   entrances on the pavements, stairs down to a ticket hall and platforms; people on the pavements, waiting on
   platforms, getting on and off, each with somewhere to go (home, work, a shop) on a daily round. Trains are pictures
   with riders, not vehicles (the vehicle system is TwitchTheSniper's).
3. **Places**: buildings you can go into at street level (a station hall, a market, a bar, a hotel lobby with lifts to a
   roof), signs and neon, rooftop pads where the stand-in flyers land, the docks at the edge.
4. **Getting there**: Slopolis in a star system (a body of Frostreach, near the story's home), landing pads, traffic in
   its sky; the land beyond the last street (quarries, a road out).

## Not yet

- The city is not on the wire's map fingerprint: a host and a client on different builds could disagree about walls.
- Nothing of it can be dug, built onto by sockets or destroyed: it is scenery that collides.
- Bots have no nav graph on it (planetoids have none).
- The square's trees and the columns are simple low-poly shapes; no foliage system on planetoids yet.
