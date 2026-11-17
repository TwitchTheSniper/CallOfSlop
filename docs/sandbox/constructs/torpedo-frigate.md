# The Zero Day torpedo frigate (the IT Department's warship)

Karl (2026-11-15): a state-of-the-art faction torpedo frigate, sleeker and bigger than the Halberd (since removed), with countermeasures,
a torpedo bay on each side, a railgun and a drive the width of the ship; faceted corners and stepped armour like his
reference picture (the MORN 251 cutaway), decks running the length of the ship, and doors and fittings you'd expect on a
spaceship. The faction is the IT Department, known for powerful, state-of-the-art ships: pale skin, dark armour slabs,
link-blue light strips, server racks aboard. It is an enemy ship, but it builds and previews like any other.

Generator: `Source/PartKit/Generators/TorpedoFrigateParts.cs` (`--gen-parts torpedo_frigate_parts`), which writes
`Constructs/Parts/ZeroDay/*.json` and `Constructs/Blueprints/ZeroDayFrigate.json`. Change the generator, never its output.
Built of big parts that share the body's origin, each drawn whole (a deck's floor, walls,
hull band and hidden boxes), then broken into welded pieces (below), with furniture on their sockets. About 53 m long, 17 m
across the sponsons, 15 m tall on its legs, 251 parts.

## Breaking up

Karl (2026-11-16): split it into welded pieces (as the Halberd was), so a hit takes a piece, not a deck. Each deck part is still
drawn whole by its builder (`LowerFore()` ...), then `SplitDeck` deals its boxes out: the **floor** (and what stands loose on
it) keeps the part's name; each **wall run** (every `WallRun` call is recorded) is a Wall piece with its door frames and what
is on its faces; an upper deck's **ceiling** and its light strips are one piece; and everything outside the rooms' outline
(the skin, its armour and chine rails, and the hidden void behind them) is a **Hull** piece, one a side per station band
(8, 14, 20, 30, 38, 42 m). `SplitBands` cuts the keel and the spine at the same stations; every keel length is a tank
(14,000 l shared out), so a lift jet takes fuel from the length it hangs on, and the aft length keeps the keel's name and
the fuel pipe to the drive. (A tank is disabled when it breaks, not knocked off, and can cook off: a keel length shot through stays on as a dead, empty piece.) The bow, stern and torpedo sponsons stay whole. A socket goes to the piece whose box face it
is on (`DealSockets`, `SockOwner`), the blueprint's `Hang` wrapper puts furniture on that piece, and `Connect` gives every
two pieces whose solid boxes touch a facing socket pair: one places a piece (`PlaceOrder`, out from the keel), the game
welds the rest. Pieces of one part never overlap (the side walls start where the front wall ends), and the walls' dado
stands 3 mm off the floor, so `--check-vehicle` finds no coplanar faces between pieces. The flight check flies it as it
did whole (climbs 7.6 m in 2 s, lift 1.58x its weight).

The crew's lockers are the **Zero Day Locker** (Karl, 2026-11-16: the shared open-shelf Locker looked bland): a closed
graphite cabinet with a raked top, louvres, a recessed pull, a badge reader with its lamp, a lit name strip and a blue edge
light down its hinge side. Only its door takes the blueprint's paint (yellow at the airlock, blue in the cabins, white in
the med bay).

## The hull

A lofted hull of six facets (keel, four sides, spine) through stations from the chisel bow (z -3.5) to the stern (46.5). Neighbouring facets' ends are staggered (`FacetStagger`) so no two plates share a plane where they cross.
Each half profile is five points; `At(z)` blends them, and `SkinHalf(z, y)` gives the inner half width at a height, so
`MustFit` refuses a room that would poke through the skin. On the facets go bevelled armour slabs (`Plates`), chine rails
(`Chine`) and raked shoulder blocks (`Shoulder`). Voids (`Void`) are hidden boxes out to the skin, their ends kept 4 mm
apart from the next band's.

## The decks

Two decks run fore and aft (floors at 0.25 and 3.5 m, ship frame), each in a fore part and an aft part split at z 25:

| Part | What is in it |
| --- | --- |
| Zero Day Lower Deck Fore | the server room (Server Racks), the armoury, the fore end of the torpedo room with a bay door each side, the fire buttons; the ladder up to ops |
| Zero Day Lower Deck Aft | the rest of the torpedo room, the airlock over the boarding shaft (starboard), the suit bay (port), engineering (the drive's works as one cylinder forward from the aft wall, the lift jets' Drive Core M, reactor, board, batteries) and the ladder up to the mess |
| Zero Day Upper Deck Fore | the sealed bridge (no glass: a view screen over the dashboard, camera screens each side; two Flight Couches, the flight computer), ops (the forward gun station, a plot table, the flares button), cabin A and the captain's cabin |
| Zero Day Upper Deck Aft | cabin B, the head, the med bay, the mess with the aft gun station |

Around them: the Keel (14,000 l of fuel, legs, lift jets, the boarding shaft), the Bow (armoured over the bridge, retro jets), the Spine
(roof, the railgun's trough, the shuttle's cradle with its Docking Clamp, mast, antennae, flare launchers), the Stern (the engine bay: 9,000 l, fins), the Drive
Collar (600 l, the shroud and plate the width of the stern) and four Drive Bells (2.25 MN each, 9 MN in all).

## Drives and their works

The Zero Day is the reference ship for ship works (docs/sandbox/design/ship-works.md, section 15): one engine a bell, all
inside the hull, in one room with engineering.

- The four **Zero Day Drive Bells** (2.25 MN torches) hang on the **Drive Collar**, a mount on the Stern's aft face. Each bell
  needs a Drive Core and a Fuel Pump (ShipWorks.json's `drives` table), and its own engine is bolted to the collar plate's
  inner face on that bell's axis: a **Drive Core M** with a **Fuel Pump M** behind it, running forward into the **engine
  bay** (the Stern piece: no aft wall to engineering, a stepped floor, gratings, a fuel header overhead). Engineering's aft
  wall is open, so engineering and the bay are one room and every core is reached standing from its door. Nothing is shared:
  wreck one core and only its bell falls to a quarter.
- The four **Lift Jets** (1.7 MN, from 1.5 since the shuttle rides on the spine) on the keel and the two **Retro Jets** (1.2 MN) in the bow's cheeks are whole parts with
  no works (Karl, 2026-11-16: main drives only). A breakdown on a lift jet is mended at the jet, outside.
- With its works whole a hull drive burns 2 l/s a MN (each bell 4.5 l/s). Shoot out a works piece and its bell's cap falls:
  the look line, the Engineer's Panel and `--check-vehicle`'s `works:` lines say which piece. Breakdowns pick the works
  pieces, not the bells (ship.md "Breakdowns").

## Weapons and countermeasures

- Two torpedo sponsons (Zero Day Torpedo Bay L/R), each a room off the torpedo room through a bulkhead door, with a
  six-round drum Ordnance Rack loaded with designed torpedoes (radar seeker, HE warhead) that leave through the launch
  port in the sponson's front face. The fire buttons are on the torpedo room's walls.
- The Zero Day Railgun along the spine, fixed forward and aimed by the ship (the pilot fires it). Each slug takes 15 MJ from the Capacitor Bank M in engineering, aft of the reactor (ship-works.md 8; energy guns fire from capacitors only): two full slugs and a bit, one refilled in 10 s.
- Four Zero Day PDCs on barbettes on the beam, run from the two gun stations (ops and the mess). Each turns on a **Turret
  Ring S** whose trunk goes down the barbette's well, fed from its own **magazine** (ship-works.md 5.2): an Ammo
  Container standing inside against ops' or the mess's side wall by its barbette, its 0.7 m3 box (2,400 rounds as
  sealed) in the hull's void behind the wall, sealed by the wall, the deck and the void round it (`MagazineAt` cuts the
  void). The feed is laid by the generator (`MagazineFeed`: up the wall, through it, the void and the hull into the
  barbette, up into the trunk's foot), since the router can't pass solid void. Nothing is bolted to the guns outside, so
  shooting a turret off leaves its rounds inside; a hit on the magazine through the hull cooks it off there. A store
  whose blueprint fills it with ammo comes full whenever the game builds it, in Survival too (Karl, 2026-11-17: an
  enemy frigate must be able to fire); one a player's Building Station fits comes empty and is filled by hand
  (`ConstructBuild.SetConfig`, `ConstructWorld.PlayerBuild`).
- Four Zero Day Countermeasure Launchers (flare guns) on the spine, all fired by the flares button in ops.

## Getting aboard

The boarding ladder climbs from the ground up the belly shaft into the airlock; its inner door is aft of the shaft so
both landings are clear. `--check-building "Zero Day Frigate"` passes (327 m2 a standing person can reach and leave,
from the ground up the boarding ladder and the two ladders between the decks). The legs leave 2.5 m under the keel so
people can walk beneath it to the shaft. A ladder in the mess climbs through the spine into the Shuttle on its
cradle ([shuttle.md](shuttle.md)).

## Flames

A thruster with no `"flame"` gets one from its force (`ConstructParts`: radius `sqrt(F) * 0.002 + 0.05`), which at frigate forces is far wider than
the nozzle: 12 m across from the middle of the drive. So each Zero Day thruster names its own: one per drive bell (1.15 m, 9 m long) and one
from each jet's mouth (0.4 m times its scale).

## The crew (an enemy that flies and fights by itself)

Karl's roadmap review (2026-11-15), step 5: one enemy ship that flies and fights by itself. Any craft with a Flight Stabilizer
can have one: the stabilizer's **AI crew** setting (Crew group: Off, Fights strangers, Fights everyone). The blueprint
**Zero Day Frigate (crewed)** is the Zero Day with it set to Fights everyone, so from the parts menu it is an enemy at once,
its spawner included. Code: `Source/Constructs/ConstructShipAi.cs` (the crew), `Source/Game/GameSandboxShipAi.cs` (which
bodies have one, who each is after; server only).

- **It works the ship's own seats**, as players would, so nothing new goes over the wire and a ship needs no special parts.
  The pilot's seat (the stabilizer's) flies it: the crew turns the stabilizer's Mouse aim on and looks at the target, so the
  ship turns to face it; Shift and Ctrl hold it a few metres over the target (never under 35 m over the ground or into a hill
  ahead); W and S hold a closing speed that falls to nothing at 220 m, inside its guns' 300 m; in that band Q and E slide it
  one way and then the other every few seconds. The gunners' seats look where their rounds meet the target (led for its
  speed) and fire when a gun bears within 4 degrees (2.5 for a fixed gun such as the railgun) and nothing of its own hull is
  in the way. The Buttons wired to Ordnance Racks launch one torpedo every 8 s, taking turns, from 350 m to 2 km with the
  target within 15 degrees of the nose, at a ship or a car only (a seeker wants something that moves); the Button wired to
  the flare launchers goes when a missile steers for it inside a kilometre.
- **Targets.** The nearest living player within 2.5 km: the body they sit or ride in, else themselves. Fights strangers
  leaves out the ship owner's faction (a nobody's ship fights everyone). Anyone aboard it is not a target.
- **With nothing to fight** it holds where it is, and after half a minute comes down where it is.
- **Boarding.** A player in the pilot's seat has the helm: the crew stands down and Mouse aim goes back as it was. A player
  in a gunner's seat has that seat. Setting it Off stands the crew down for good.
- **Not yet:** saving a crew's state (it is the setting, so a loaded ship has its crew again), flying in space past the sky's
  edge (a ship there is a SpaceFlight particle; the crew stops), the radar lock (PR #211: racks launch on what their
  seekers see), defending anything, fleets or factions.
- **Seated crew (2026-11-16, the bot brain: docs/sandbox/design/bot-brain.md, Source/Crew/).** The setting now puts a crew in
  the seats: a pilot at the helm, a gunner at each gun seat, spares on the couches. The flying and gunnery above work a seat only
  while a living member sits at it: shoot the pilot and the helm keeps its last input for 3 s, then a spare walks over and takes
  it (with nobody left it holds); shoot a gunner and that gun falls silent until a spare takes the seat. Near a player the crew
  are bot bodies in their seats; further off records, drawn as stand-ins. A crew is saved with its ship.
- **The check:** `--crew-check [SHIP] [--target NAME] [--range M] [--seconds S] [--still] [--crew-scale N]` (`Source/Crew/CrewCheck.cs`,
  was `--ship-ai-check`) flies a crew against a target ship on the flight check's flat floor and prints a line every two seconds. On 2026-11-15 the
  Zero Day against a second Zero Day 1200 m off (sliding about at 40 m): it lifted off, turned onto it in 6 s, launched its
  first torpedo at 1180 m, closed at 23 m/s (its stabilizer's top speed) to 236 m, fired 1204 rounds until its ammunition was
  gone and took the target to 98% of its health.
