# The Cloche greenhouse ship

The farmers' greenhouse ship (Karl, 2026-11-21: a picture of a long red farm ship, "all new parts, new hydroponics, new nacelles,
new everything", and "build this like-for-like on the exterior"; he picked the farmers on the card). Generator
`Source/PartKit/Generators/ClocheParts.cs` (`--gen-parts cloche_parts`); blueprint `Constructs/Blueprints/ClocheGreenhouseShip.json`
("Cloche Greenhouse Ship"); 53 parts in `Constructs/Parts/Cloche` (unlisted, Categories.json's Ship name rule). Nothing comes from
another ship or the stock fittings: its seat, dashboard (the flight computer), lamps, ladders, door, power plant, lift jets, drives,
grow racks and beds are its own. It has its own gallery page, apart from the Ship gallery (the artifact in project memory).

## Shape and look

- A long boxy hull 10 m wide, 6.6 m to the roof, chamfered top edges, a dark chassis band and two skids under it; dark ribs wrap the
  sides and chamfers every 3.2 m (`Ribs`). A taller aft block (9.3 m) over the engine room, its corners chamfered at the stern.
- Two glass barrel vaults on the roof (`Bays`, three bays each): a six-faceted vault on cream frames, glass walls, gable ends, two
  rows of grow beds either side of a grating walkway, yellow frames and rails inside.
- A cream bus front on the bow (`Cab`): split windscreen and glass double door, dark bumper and lamps, a visor roof you walk on, a
  beacon. A railed balcony across the bow at the cab roof's height, a railed step each side at the deck's height, ladders down.
- Two eight-sided water tanks strapped low on each flank (cream ends and band, red body, pipes up into the hull); a red nacelle each
  side of the aft block on a dark pylon, cream intake ring round a dark grille, a pipe in from the block.
- On the aft roof: two stacks, three masts with lamps, a radome on a yellow lattice tower (fore port corner), a grille.

## Inside and flying

- One deck the length of the hull at 1.3 m: the entry room behind the cab (the side door off the port step), the grow hall (three
  rows of Cloche Grow Racks) and the engine room (the power plant). A hatch ladder up through the roof into each greenhouse.
- Sixteen Cloche Lift Jets (480 kN each, under the 500 kN that needs works) under the keel, which carries their fuel; a Cloche Drive
  (450 kN) in each nacelle's tail on the nacelle's own fuel. `--flight-check`: flies, lift 2.9x its weight.
- Welded pieces as the Dibber (`Connect`, `PlaceOrder`): keel in five lengths, sides and roof in four, bow, cab, balcony, steps, two
  bulkheads, the aft block's sides, roof and stern, six greenhouse bays, four tanks, two nacelles. Every part says
  `"collider": "boxes"`: its hidden and solid boxes collide, the faceted looks are drawn only (a 12- or 16-sided look hull crashed the
  narrow phase in the first flight check).
- Checks run: `--check-vehicle` 0 coplanar faces between parts; `--check-building` ok (263 m2); "No z-fighting", "Assembling" and
  "Compound collision" pass.
