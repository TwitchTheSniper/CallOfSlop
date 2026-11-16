# The Zero Day torpedo frigate (the IT Department's warship)

Karl (2026-11-15): a state-of-the-art faction torpedo frigate, sleeker and bigger than the Halberd, with countermeasures,
a torpedo bay on each side, a railgun and a drive the width of the ship; faceted corners and stepped armour like his
reference picture (the MORN 251 cutaway), decks running the length of the ship, and doors and fittings you'd expect on a
spaceship. The faction is the IT Department, known for powerful, state-of-the-art ships: pale skin, dark armour slabs,
link-blue light strips, server racks aboard. It is an enemy ship, but it builds and previews like any other.

Generator: `Source/PartKit/Generators/TorpedoFrigateParts.cs` (`--gen-parts torpedo_frigate_parts`), which writes
`Constructs/Parts/ZeroDay/*.json` and `Constructs/Blueprints/ZeroDayFrigate.json`. Change the generator, never its output.
Built the way the Halberd is (frigate.md): big parts that share the body's origin, each drawn whole (a deck's floor, walls,
hull band and hidden boxes), then broken into welded pieces (below), with furniture on their sockets. About 53 m long, 17 m
across the sponsons, 15 m tall on its legs, 247 parts, 211 t.

## Breaking up

Karl (2026-11-16): split it into welded pieces like the Halberd, so a hit takes a piece, not a deck. Each deck part is still
drawn whole by its builder (`LowerFore()` ...), then `SplitDeck` deals its boxes out: the **floor** (and what stands loose on
it) keeps the part's name; each **wall run** (every `WallRun` call is recorded) is a Wall piece with its door frames and what
is on its faces; an upper deck's **ceiling** and its light strips are one piece; and everything outside the rooms' outline
(the skin, its armour and chine rails, and the hidden void behind them) is a **Hull** piece, one a side per station band
(8, 14, 20, 30, 38, 42 m). `SplitBands` cuts the keel and the spine at the same stations; every keel length is a tank
(14,000 l shared out), so a lift jet takes fuel from the length it hangs on, and the aft length keeps the keel's name and
the fuel pipe to the drive. The bow, stern and torpedo sponsons stay whole. A socket goes to the piece whose box face it
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
| Zero Day Lower Deck Aft | the rest of the torpedo room, the airlock over the boarding shaft (starboard), the suit bay (port), engineering (reactor, board, batteries) and the ladder up to the mess |
| Zero Day Upper Deck Fore | the sealed bridge (no glass: a view screen over the dashboard, camera screens each side; two Flight Couches, the flight computer), ops (the forward gun station, a plot table, the flares button), cabin A and the captain's cabin |
| Zero Day Upper Deck Aft | cabin B, the head, the med bay, the mess with the aft gun station |

Around them: the Keel (14,000 l of fuel, legs, lift jets, the boarding shaft), the Bow (armoured over the bridge, retro jets), the Spine
(roof, the railgun's trough, a dorsal block, mast, antennae, flare launchers), the Stern (9,000 l, fins) and the Drive
Array (four bells in one collar the width of the stern, 9 MN).

## Weapons and countermeasures

- Two torpedo sponsons (Zero Day Torpedo Bay L/R), each a room off the torpedo room through a bulkhead door, with a
  six-round drum Ordnance Rack loaded with designed torpedoes (radar seeker, HE warhead) that leave through the launch
  port in the sponson's front face. The fire buttons are on the torpedo room's walls.
- The Zero Day Railgun along the spine, fixed forward and aimed by the ship (the pilot fires it).
- Four Zero Day PDCs on barbettes on the beam, run from the two gun stations (ops and the mess).
- Four Zero Day Countermeasure Launchers (flare guns) on the spine, all fired by the flares button in ops.

## Getting aboard

The boarding ladder climbs from the ground up the belly shaft into the airlock; its inner door is aft of the shaft so
both landings are clear. `--check-building "Zero Day Frigate"` passes (327 m2 a standing person can reach and leave,
from the ground up the boarding ladder and the two ladders between the decks). The legs leave 2.5 m under the keel so
people can walk beneath it to the shaft.

## Flames

A thruster with no `"flame"` gets one from its force (`ConstructParts`: radius `sqrt(F) * 0.002 + 0.05`), which at frigate forces is far wider than
the nozzle: 12 m across from the middle of the drive. So each Zero Day thruster names its own: one per drive bell (1.15 m, 9 m long) and one
from each jet's mouth (0.4 m times its scale).
