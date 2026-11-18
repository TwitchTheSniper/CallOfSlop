# The Ministry spaceport

Karl, 2026-11-13: "add about 2 km away a Ministry Spaceport: tall, Brutalist, with multiple landing points", then "a
small space ship which you can steal, or you can speak to the person and help them with something which allows you to
get off the planet onto another".

## What is there

On Glacier, about 2 km from the penal colony out of its main gate (east), on a dark graded hardstand of its own:

| Piece | Blueprint (part kit, `BuildingParts.Spaceport.cs`) | Landing |
| --- | --- | --- |
| **Spaceport Terminal** | Arrivals and customs in one 16 by 12 m hall split by the customs counter: the queue, benches and phone landside (front door to the colony, under its canopy), the departures desk's filing, the teleprinter and the cargo airside (door at the back-east corner to the apron). Slots in front, a high ribbon down each side. | Its roof, 3.5 m up, flat inside the lid, amber lamps on the lid. A stair up the west wall from the airside half comes out through a hatch. |
| **Spaceport Control Tower** | A blind 8 by 8 m shaft seven storeys high, slots up the stair, a glass cab on top with the controller's desk, red lamps on the lid's corners. The stair goes up in alternate flights (west, then east), each under its own floor's opening, so every flight has a storey of headroom; the last goes from the cab through the roof's hatch. | Its roof, 21.5 m up, 8 by 8 m inside the lid: small craft. |
| **Spaceport Landing Pad** (three) | 16 by 16 m of foundation laid flush with the hardstand, a touchdown square of four painted yellow bays, an amber lamp on each corner. | The ground. |
| **Ministry Courier** | The Orbiter in navy and green (`ShellParts.cs` writes it from `Orbiter.json`), parked on the first pad. | |

The Ministry's style (docs/sandbox/constructs/buildings.md): the tower's one bold move is its height, and the cab is
the one place the Ministry looks out.

## Where it stands

`Story/Opening/Spaceport.json` (`Game/GameSpaceport.cs`): built with the colony on a fresh story world on its `"map"`,
after everything round the colony, so nothing else is in its way. It looks along `"toward"` (the colony's frame: `[1, 0]`
is out of the main gate) up to `"spread"` degrees either side, `"step"` apart, at `"distance"` m and `"slack"` m nearer or
further, and takes the most level spot (a little extra for each degree and metre off the line) whose land stays within
`"most"` m of the bench, with no other construct within 40 m. Its frame's front (-z) faces the colony. The bench (60 by
46 m, `"bench"`) is graded through `Game.Dig` like the colony's, raised just over the highest land under it so it is all
one dark made-ground surface, with a ramp from its front down to the land. The pieces are laid at fixed spots in its
frame (`"pieces"`, the terminal first: on a loaded world the frame is worked out from it).

On today's Glacier it lands 1965 m from the colony. Glacier is a planetoid of 8 km radius, so from the colony it is well
below the horizon: you find it by walking east, by the red lamps once over the rise, or by asking.

**Saves.** The pieces are kept in the colony buildings' list (`Game.ColonyBuilding(name)` finds them) with a mark,
`(spaceport)`, so it is built once: a world from before it gets it (and the pilot) the next time it is loaded, and a
piece taken down stays down.

## Captain Fairweather and the courier

Captain Fairweather is in the Penal Colony cast and the Ministry's staff, but his body keeps to the apron beside the
courier (the file's `"keeper"`): his home and work are there, and he walks no further than 30 m from it to meet anyone
(`StoryBody.Station`, GameStoryBodies.cs). He has been grounded three years: his Form 88 (Permission to Leave the Surface)
wants the warden's countersignature, the warden is at the colony, and a pilot may not leave the spaceport without Form 88.

- **The errand** (`Story/Topics/Spaceport.json`): ask him about the ship, why it is grounded, and offer to take the form;
  Warden Pettigrew signs it (not with Suspicion 5 or more, nor for someone who hit his staff); bring it back and he hands
  over the courier ("fairweather-signed", the file's `"handover"`): the story's `Cleared` (the Ministry's deed "Ran the
  courier's errand", +8), the ship's owner is the player and the Ministry's claim on it is gone. The bearer of Form 88
  may leave the surface, and pilots may not carry forms.
- **Theft**: sit in the courier while it is still the Ministry's and it is stolen (`Game.SpaceportBoard`, from
  `StoryBoard`): the story's `Commandeer` (the Ministry's deed "Stole a Ministry ship", -35, and Fairweather's grudge),
  he shouts after you if he is about, and the ship is yours.
- Either way it is an Orbiter: it climbs out of the sky into orbit, and free travel takes it to another planetoid.
- Officer Crumb ("Is there any way off this planet?", after the rocket programme) and Sergeant Bellweather point the way.
- His lines are `Story/Lines/Spaceport.json`; what became of the ship is his `CourierGiven` or `CourierStolen` quality.

## Cost

Nothing runs per tick beyond the story's own look at who sits down. The pieces stand frozen or anchored. Drawing: the
pads and the courier are culled past 700 m like any small body; the terminal and the tower have 64 parts or more, so
like any big body they are drawn whole (merged meshes) out to 3 km when in view, even below the horizon: two merged draws.
Measured at the colony looking east (llvmpipe in the agent container, 200-frame means): constructs 15.9-17.8 ms a frame
without the spaceport, 15.6-16.2 ms with it, so within the software renderer's noise. A horizon cull for big bodies on a
planetoid would take those two draws away; it isn't done (it would be the renderer's, for every big body).

## Not done

- The stand-in traffic and faction ships don't use the pads yet; the Orbital Depot and the spaceport aren't linked.
- The tower's cab has no use beyond the look; the customs counter checks nothing.
- No storm porch; the hardstand's ramp is at its front only.
