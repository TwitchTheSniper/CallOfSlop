# The galaxy and the jump drive (phase 6)

Phase 6 of the space plan (the project file `planets/SPACE-ARCHITECTURE.md`, sections 8, 9 and 11) is "many systems".
This file covers its first part: the galaxy tier (the architecture's tier 1, GAL) and travel between systems, which Karl
decided on 2026-11-04: **a jump drive part built into a ship**. Mod patching is in docs/mods/MODS.md. Factions and ship
and station templates wait for Karl's plan. Ships keep the full local simulation wherever they are (Karl, 2026-11-04): there
is no low-attention simulation of far ships.

## The galaxy: `Source/Space/Galaxy.cs`

The galaxy is every star system of the game (`StarSystems.All`, one per `Systems/*.json`), **at most ten** (Karl,
2026-11-04: for gameplay; `Galaxy.MaxSystems`). A system's index in that list (file-name order) is its galaxy index: the
byte the wire carries, and peers check they agree on the order with `Galaxy.Hash` (every file's hash, in order).

Each system file may say where it sits:

```json
"galaxy": { "ly": [3.1, 2.8, 0.9], "arrive": { "au": 0.5 } }
```

- `ly`: the position in light-years (each axis within ±1,000). Every system's ecliptic shares the galaxy's axes, so the way
  from one star to another is the same seen from either. Default: the galaxy's middle, [0, 0, 0].
- `arrive`: how far from the star a jump arrives (`au`, `km` or `a` in metres). Default (`Galaxy.DefaultArrival`): a quarter
  past the farthest the star's own bodies get, at least half an AU. With `"at": "Cinder"` the distance is from that body
  instead (`StarSystem.ArrivalBody`): the ship arrives in a circular orbit round one of the system's worlds, a short
  flight from its sky. All five systems do this (there is no time warp to cross half an AU with).

Refused with a reason: an eleventh file, two systems less than 0.05 ly apart (the later file), unknown keys, a bad `ly`, an
arrival inside twice the star's (or the named body's) radius or past half the fixed-point reach, an `at` that names no body.

Inside a system nothing changes: positions are still 0.1 mm fixed point from that system's star (`SpacePos`). The
galaxy only places whole systems, so light-years never meet fixed point. All systems share one time (the bubble's clock):
the epochs are the same.

The shipped galaxy:

| System | File | Where (ly) | What |
| --- | --- | --- | --- |
| Dev System | `Systems/Dev.json` | 0, 0, 0 | the Planetoid (the map), Moonlet, Bluestone and Redrock (maps too) |
| Ember | `Systems/Ember.json` | 3.1, 2.8, 0.9 (4.27 from Dev) | a red dwarf: Cinder, Ashfall and its moonlet Soot |
| Frostreach | `Systems/Frostreach.json` | -4.4, 4.6, -1.7 (6.59 from Dev, 8.14 from Ember) | a white star: Glacier and its moon Rime, Shard |

| Glenmore | `Systems/Glenmore.json` | -4.6, -5.6, 1.4 (7.38 from Dev) | a yellow star: Caledon (a map) and its moon Cairn, Skerry |
| Hearth | `Systems/Hearth.json` | 5.6, -5.2, -1.6 (7.81 from Dev) | an orange star: Mille (a map, hollow) and its moonlet Crumb, Proof |

Cinder, Glacier, Caledon and Mille have maps (docs/planets/WORLDS.md): a jump arrives in orbit round them, and a ship
that flies down into their sky lands the group there ("Landing in another system" below). From Dev every system is in the Jump Drive's 9 ly; Ember also reaches Frostreach
and Hearth; Glenmore is reached only from Dev.

**In the sky.** Every system's sky shows the galaxy's other stars as steady points of their colour (the star's `color`, or
a white from its mass), brighter for a heavier, nearer star (`Galaxy.ApparentBrightness`): `SpaceSkyView.Stars`, drawn by
the sky shader's `uStarPts` / `uStarCol`.

## The Jump Drive: `Constructs/Parts/Space/JumpDrive.json`, `Source/Game/GameSpaceJump.cs`

A logic part (kind `jumpdrive`) with a `"jump"` block:

```json
"jump": { "range": 9, "charge": 8, "energy": 40 }
```

`range` is how far it reaches (light-years), `charge` the seconds it takes to charge, `energy` the kJ a jump takes for each
tonne of ship and light-year. Its `jumpTarget` setting (the configure tool) picks the system: 1 is the nearest other system,
2 the next (`Galaxy.Nearest`; ties go to the lower index).

- **Charging.** In space (a construct out of the bubble), the drive seat's jump key starts the charge and stops it again; F
  on the drive or a rising signal on its input do the same. On the ground it won't start. It draws its power while it
  charges, and puts out how far it has charged (0 to 1).
- **The jump.** Once charged, if the target is in range and the batteries hold the energy, the ship leaves its system's
  flight and arrives in the target's at the target's arrival distance from its star or its world, on the side facing where
  it came from, in a circular orbit round it (`Galaxy.Arrive`), keeping its ship id. The batteries pay the energy. If it can't go,
  the charge stops and the driver's HUD says why (out of range, not enough charge, no such target): the HUD line is worked
  out from what every peer has, so a client says the same as the host.
- **The Jumper** (`Constructs/Blueprints/Jumper.json`) is the Orbiter with a Jump Drive and three batteries (2.5 t, 900 kJ;
  a jump to Ember takes 427 kJ).

## Parked ships

The World is floats near its origin (one bubble). A ship in another system, or more than 200 km
(`Game.SpaceFarDistance`) from the World's origin in its own, is **parked** (`Construct.SpaceAway`: the galaxy system plus
one):

- Its construct waits out of the way high over the map (each in its own spot), its physics asleep, posed upright (game
  +Y is the system's ecliptic north: `Game.AwayRot`).
- It is drawn only for its own riders (`ConstructRender.AwayOnly`), who see that system's sky round them and none of the
  map (`Game.SpaceAwayView`, `SpaceSky.UpdateAway`, `Program.DrawArena`): its star as the sun, its bodies, its other ships
  and the galaxy's other stars. Everyone else doesn't see it in the World (it shows as a ship's glint in their sky when in
  their system).
- Its thrusters still push the ship and A and D turn it. A ship in another system is stepped by that system's own flight
  (`SpaceSession.Away`, made on first use) on the bubble's time (`SpaceFlight.Follow`); ship ids are shared across the
  galaxy (`SpaceShips.IdTaken`). The warp rule counts those ships too.
- A parked ship back within 180 km of the bubble's origin (in the bubble's system) is posed from its ship again and can
  fly back down.

**Landing in another system.** A parked ship that comes within the sky's edge of a world with a map, in the system it is
in, lands there as a ship does at home (`Game.SpaceLandingStep`, docs/space/SPACE.md "Landing on another planetoid"):
everyone seated aboard, the world left behind kept in the save's `planets/` folder. As the new map starts the bubble is
that system's (`Game.MakeHostSpace`): its flight, which the ship is already in, becomes the bubble's, the flight left
behind flies on as another system's (`SpaceSession.Adopt`), and the galaxy's time carries on. Free travel (a planetoid
beside the host's game) stays inside the bubble's own system.

**Saved.** A world save keeps every ship, in whichever system it flies (`SpaceAway` section: the system by name, then the
ship's primary, offset and velocity, as the bubble's own `Space` section has them), and whoever sat in one out in space
(`SpaceSeats`: they sit there again when they come back, since standing up in orbit would drop them). On load a ship in
another system is in that system's flight again and its construct is parked; a ship whose system this build doesn't have
is lost and its construct comes back into the bubble where it was saved. An older build skips both sections.

Not done yet: arriving in another system's bubble (landing works within the bubble's system: SPACE.md "Landing on another
planetoid"), several bubbles.

## The wire (protocol 128)

`Msg.Space`'s Bubble message ends with `Galaxy.Hash`; a client whose galaxy differs follows only the bubble's own
system's ships. A Ships packet carries one system: its galaxy index after the tick, then per ship its id, the construct
it pictures (0: none), a parked byte, and the `ShipState` (in that system). The host sends one packet per system with
ships, each with its own cursor.

## Testing

The "Galaxy" self-test section (`Source/App/SelfTestGalaxy.cs`): the shipped galaxy loads, distances and the nearest
order, the arrival orbit, the refusals; then a real host and client with lag and loss on the Planetoid: the Jumper in
orbit, the jump key refused with too little charge, the charge, the jump to Ember (flight, id, arrival, energy), the
host's view of Ember's sky, the client following it there, W in Ember, no allocation with a ship away, the jump home
(parked far out) and unparking near the planetoid.
