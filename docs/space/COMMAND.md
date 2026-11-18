# The command view and ship orders

The command view is the game's map: the star system drawn as a hologram over the dimmed game, on one wheel from a ship's
decks out to every planet's orbit. A player picks a ship in it, reads what the ship is doing, and tells it where to go; a
ship with a working flight computer then flies there by itself. It is the first piece of the RTS side of Survival (Karl,
2026-11-16): moving ships from A to B without sitting in them.

- **Key:** Home (`GameAction.CommandView`, rebindable; M is the Mirror Table's). Esc closes the order menu, then the view.
  Sandbox only, on a map that is in a star system (`Game.Space`); elsewhere the key says why not.
- **Code:** the picture `Source/UI/UiCommand.cs`, the ships in it and the ship card `UiCommandShip.cs`, the crews on it `UiCommandCrew.cs`, the order menu
  `UiCommandOrders.cs`, the key `Source/App/Program.Command.cs`; the orders `Source/Game/GameNav.cs`, trips between
  planets `Source/Game/GameNavTrip.cs`; the steering maths `Source/Space/NavGuidance.cs`, the transfer planner
  `Source/Space/NavTrip.cs`.
- **Cost:** closed, one bool check a frame. Orders cost the host one pass over the order list a tick (at most
  `Game.MaxNavOrders`, 64) and a few vector sums per ordered ship, whatever the ship's size, plus one pass over its
  thrusters.

## The picture

An orthographic view: a focus, a turn and a tilt about the ecliptic's north, and metres to pixels (`_cmdSpan` is the
metres up the screen, from `CommandMinSpan` 12 m to three times the system's reach). Nothing is drawn in 3D
by the renderer: it is ImGui draw-list lines over the game's frame, so it works on any map and costs the GPU nothing.
The game's own HUD, the hands and the noclip note are hidden under it, and the view counts as a menu (`MenusOpen`):
the cursor is free and the game takes no input.

| Input | What it does |
| --- | --- |
| Wheel | Zoom (x1.32 a notch), eased |
| Left drag | Pan (the focus is then pinned: "FOCUS PINNED") |
| Right drag | Turn and tilt (the tilt stops at straight down) |
| Click | Pick a craft or a body (its card opens on the right) |
| Double click | Centre on it |
| Right click | An order for the picked ship: on a world, an orbit; on a place in space, go there; on another craft, dock with it or meet it alongside |
| SHIP / WORLD / MOONS / SYSTEM | Jump to that zoom, focus back to automatic |

**The focus follows the zoom** while it is automatic: close in it is the picked ship (the player's own position with none
picked), further out the body that ship is round, then that body's planet, then the star. It glides when it changes
(`_cmdGlide`), so zooming out from a ship in Glacier's orbit ends on the whole system without a jump.

**What is drawn, by zoom** (each thing fades in by its size on screen, never by a named level):

- Range rings on the ecliptic plane through the focus at round distances (1, 2, 5 times a power of ten), spokes every
  30 degrees, and a scale bar.
- Bodies: a dot with its name until it is 5 px across, then a globe: a graticule turning with the body's spin, and for
  a world with a map the dashed "SKY EDGE" ring where its sky ends and space begins. Moons hide in their planet's dot
  until their orbit opens up. Every body's orbit is its Kepler ellipse.
- Craft: a dart (in space) or a diamond (in a sky), with brackets and a slow ring on the picked one, its name on a
  leader line, and the conic it is on (`KeplerOrbit.FromState` round its primary): cyan for a closed orbit with LOW and
  HIGH marks, red where it meets the ground. Closer than about 20 px a craft is its **hologram**: the ship's own parts
  as outlines by deck (`CmdHoloBuild`: at most 72 x 72 cells and 12 levels, 6000 lines, four ships cached), with its
  thrusters' flares when they burn and a ring on the flight computer.
- An order: amber. An orbit order is a dashed circle at its height, a go-to a diamond with running dashes from the ship.

**Craft** (`Ui.CmdIsCraft`) are constructs with a flight computer, with thrusters and a helm, or already in space; wrecks
and carried pieces are not. The left list ("CRAFT IN RANGE") has them all; a click picks.

## The ship card

What a commander wants to know about a ship without boarding it (`CmdCardRead`, read four times a second):

| Row | What it says |
| --- | --- |
| Head | Name (from its flight computer's part name and its number: "MAGPIE 1"), mass, length, parts, owner; "BURNING" while the drive is lit |
| STATUS | Landed on, flying over, in space round, or parked far from which body |
| FLIGHT | Height over the ground and speed (round its primary in space) |
| ORBIT | Low and high points and the time round; "FALLING BACK: IT MEETS THE GROUND" or "ESCAPING" when it isn't an orbit |
| HULL, FUEL, POWER | Bars: parts still working, litres of fuel of its tanks' room, stored energy |
| DRIVE | Acceleration at full thrust (and in g) and the thrust itself |
| BURN | Time at full thrust on the fuel aboard, and the change of speed that buys (dV) |
| CARGO | What its holds carry |
| SYSTEMS | Flight computer (and whether its autopilot is on), jump drive, guns and thrusters, counted |
| ORDER, NOW | Its order and what it is doing about it; why it takes none ("NO FLIGHT COMPUTER") |
| FACTION, COMMAND, PEOPLE, CREW | Who holds it and how they stand with you, its group and admiral, the people aboard, its crew ("The crews layer", below) |

Buttons: CANCEL ORDER, a one-click "ORBIT <BODY> LOW", CENTRE. A picked body has a small card of its own (what it is a
planet or moon of, radius, gravity at the ground, how far out it is and its time round, whether it can be landed on).

## The logistics layer

The user (TwitchTheSniper), 2026-11-16: linked logistics (docs/sandbox/design/linked-logistics.md) as a layer of its own
over the map, which the **LOGISTICS** button (top middle, right of the zoom presets) turns on and off. It stays as it was
when the view is closed and opened again. Code: `Source/UI/UiCommandLinks.cs`.

- **Only what is linked now is drawn.** A factory (a body) is a node when one of its link ends (Transmitter, Receiver,
  Relay) is answered by a partner. Unpaired ends, ends whose pair doesn't answer (unpowered, out of reach), wrecks and the
  bodies of players outside your faction are not drawn. "NO LINKED NETWORKS HERE" under the button says the layer is on
  and empty.
- **A node** is a green square with a dot (amber with a turning ring when lines wait for a ship; a Relay has a ring of its
  own). Its label: the factory's name (its link end's name, else "CONSTRUCT 12") and a line: the pair's code, LINK or
  RELAY, how many partners answer, DOCK or NO DOCK, and WAITING FOR A SHIP. Labels of factories closer than 26 px to one
  already labelled are left out (factories round one world at a planet's zoom).
- **Links:** two nodes of this world are joined by a green line with a dot running along it. A Relay's partner on another
  world is a dashed line towards that world's planet (cut at the screen's reach), labelled with the far end's name and
  world ("ASLEEP" when nobody is there); a partner seen nowhere (another player's body) is a short stub with its name.
- **Route legs:** a ship with a Route Computer on a running route has an amber dashed leg to the stop it flies to (its
  body here, or that world's planet), labelled "ROUTE: TO <STOP>".
- **Data:** the parts' own state (`LinkPart`, which the host makes and CBodyKind.Link sends). Protocol 330 added
  `LinkPart.Far` (`LinkFar`: an end's answering partners, a Route Computer's stops, with world, body, name and flags), made
  by `LinkLedger.FarOf` and `LinkRoutes.FarOf` in the 1 Hz publish, an array only when it changed. A client has only its
  own world's bodies, so another world's end is known by this alone. The layer's own filter is the owner check
  (`Factions.Shares`, or nobody's); link parts' panel state still goes to every client, as before.
- **Cost:** off, one bool check. On, the bodies with link parts are gathered four times a second into fixed arrays (64
  factories, 128 links, 32 legs; `LinkedLogistics.PartCount` is kept per rebuild); a label string is made only when its
  key changes. A frame projects the nodes and draws lines.

## The crews layer

Karl, 2026-11-18: "visualising crews etc for the ships on the RTS view", in the view's own style. The **CREWS** tickbox (top
middle, left of MISSILES; on by default) turns the map's part of it on and off; the card's rows are always there. Code:
`Source/UI/UiCommandCrew.cs`.

- **Under a craft's name:** who holds it and its crew in one line: "FLAGSHIP  MILITIA G1  CAPT ZED  CREW 10", "YOUR CREW  8 ABOARD"
  or "PRIZE  3 PRISONERS". Its colour is how that power stands with you, as the faction window reads it: red below 0 (its crews
  fight you), amber wary (under 20), cyan neutral (under 60), green friendly, yours or a prize.
- **Groups:** a chevron over each flagship, and a dashed line running from every other ship of its group to it.
- **On a hologram:** a dot at each seat a crew member works with no body (the stand-ins), and one at each body aboard: a person
  green with a ring, a crew body in its power's colour, a ring turning on whoever has the helm.
- **On the card:** FACTION (the power and its standing in words and number, YOUR CREW, PRIZE, PLAYER BUILT or NOBODY), COMMAND
  (its group and its admiral: "ADMIRAL IKE'S FLAGSHIP", or "UNDER ADMIRAL IKE" for the others), PEOPLE (players in its seats,
  PILOT and SHELL marked, and how many stand on its decks), CREW. A crew of yours or a prize is listed member by member (up to
  eight, then "AND N MORE"): job, name, a ten-cell health bar and what they are doing (AT THE HELM, ON A GUN, MOVING, FIGHTING,
  PRISONER, OFFICER ...). Anyone else's crew is only its captain and how many are alive, and how many sit at stations as
  stand-ins: what a peer can see from outside.
- **Multiplayer:** nothing new on the wire. It reads what every peer already has: `Game.CrewInfo` and `CrewStandIns`
  (Msg.CrewSeats), `Game.CrewRosters` (Msg.CrewRoster: own crews and prizes only), the factions table's standings, and the players.
  `Game.PlayerIsBot` tells a crew body from a person on a client from the host's roster byte.
- **Cost:** off, one bool check. On, the map line is made only when its crew, standing or command changes (a key per craft);
  the group lines are a pass over the flagships on the map; the card's rows are made with the rest of the card, four times a second.
- **Window tool:** `crewgroup POWER` (every crewed body of that power, in one patrol group) and `crewown` (the last crewed body
  becomes yours) are test states for it, for example:
  `--map Glacier --calm --blueprint "Zero Day Frigate (crewed)" --do "look 90 0; spawnbp Zero Day Frigate (crewed); look 180 0; spawnbp Zero Day Frigate (crewed); wait 120; crewown; crewgroup Militia; wait 300; command 500; cmdpick 3"`.

## The missiles layer

Karl, 2026-11-17: "Missiles show up on command view. Add a tickbox to show missiles, their target and their projected
trajectory/flight path". The **MISSILES** tickbox (top middle, left of the zoom presets) turns it on and off; like the
logistics layer it stays as it was when the view is closed and opened again. Code: `Source/UI/UiCommandMissiles.cs`.

- **What is drawn:** every missile in flight (`ShellKind.Missile`: a Missile Rail's, and any designed missile or torpedo
  off an Ordnance Bench, MIRV warheads too) as a dart along its way; a fine dotted line to what it steers for, with a
  turning ring on a body (a small ring on a flare that drew it or on a point); and its path ahead as a solid line fading
  with distance, with a cross where it meets its target. Red when the target is yours or your faction's ("INCOMING"),
  orange otherwise. The label: what it goes for (the craft's call sign, FLARE, POINT or NO LOCK), how far, and how soon at
  its closing speed. Under the tickbox: how many are in flight.
- **The path** (`CmdMissilePath`) is the missile's own flight stepped forward in 40 steps of whole ticks: gravity, its push
  while burn is left (after its wait), a designed weapon's drag and its turn rate (fins by the air, gimbal while burning),
  turning towards its target where that will be, the target moving on at its velocity now, with the same 3 s lead the
  flight uses. It looks 1.3 times its time to target ahead (3 to 90 s, never past its life). Stages still to come and wings
  aren't stepped: it is what the missile would do as it is now, so a target that turns makes it bend.
- **Multiplayer:** nothing new on the wire. Every PC already flies every shell from the launch event and ShellSteer (push,
  turn, burn, target, aim, a flare drawing it), so a joined client draws the same missiles from its own copy.
- **Cost:** off, one bool check a frame. On, at most 96 missiles a frame, each 40 steps (a gravity and an air read a
  step); a label's text is made at most four times a second.

## The factions layer

The FACTIONS button (right of LOGISTICS) turns on the NPC powers' map (docs/sandbox/design/faction-controllers.md phase 4; Karl's answer 3): each
planet ringed in its holder's colour with its tag and share of control (a dashed ring in the leader's colour and white when contested), and the faction
groups this player knows of as triangles in their power's colour, labelled with a guess at what they do (STRIKE, DEFENCE, GARRISON, RETURNING) and
their ships. Fog of war (the user's answer 7): a group shows only while it is on this player's world within 8 km of them, or for five minutes after the
news named it (dim, with how many minutes ago, where it was then). A group of a power hostile to the player out to take a site, or within 3 km, is red
with brackets and THREAT. It all comes from `Msg.FactionMap`, once a second per player (`Game/GameCrewTerritory.cs`); labels are made only when it
changes. Code: `Source/UI/UiCommandFactions.cs`. Window tool: `crewterritory [near|leave]` (`Program.WindowShotPlay.cs`).

## Your fleet groups

Bottom left, the FLEET GROUPS panel (docs/sandbox/design/faction-controllers.md phase 6): your own hired crews' ships as groups, you (or a hand of
yours, job Admiral) their admiral. Pick a ship of yours with a hired crew: NEW GROUP: THIS SHIP, or NEW GROUP: CREWS NEAR ME (every one within 3 km),
or ADD THIS SHIP to the picked group. A group's row says its order and how many of its ships hear you on the fleet net (aboard a ship with a radio part
you reach 25 km, else your hand set 1.5 km; ships pass it on to each other); OUT OF REACH: no order gets through. Its buttons: HOLD, FORM ON ME,
PULL BACK, STAND DOWN; COLUMN, LINE, WEDGE, SCREEN and the spacing; DOCTRINE (Crew/Fleet.json's, cycled: caution, discipline, skill, succession);
TASK (a civilian task: its warships screen the flagship, which keeps going); ADMIRAL (you, or each hand in turn: a hand keeps command while you are
away, and your orders must reach its ship); MAKE THIS THE FLAGSHIP and TAKE THIS SHIP OUT for the picked ship. With a group's ship picked, a right
click adds GROUP orders to the order menu: on a craft ESCORT, ENGAGE (focus fire), SCREEN, FORM ON IT, CLOSE TO BOARD; on the ground GO HERE, PULL
BACK HERE and a route's stops (LOAD, UNLOAD, MINE, DELIVER, PASS THROUGH, each with the panel's work time; the route loops). Your faction's leader may
command your groups too. A nav order given to a group's ship takes it out of the group. Code: `Source/UI/UiCommandAdmiral.cs` (hooks in
`UiCommand.cs`, `UiCommandOrders.cs`), the host's side `Source/Game/GameCrewFleetPlayers.cs`. Window tool: `crewadmiral ...`.

## Orders

Eight kinds (`NavKind`), each for one ship, given by any player to any ship that takes orders. **A ship takes orders
when it has a working Flight Stabilizer** (the "flight computer"; `Game.NavProblem` says why not: a wreck, none fitted,
wrecked, or held down). Orders are the host's (`Game.NavOrders`); they are not saved and end when the map changes.

| Order | From the menu | What the ship does |
| --- | --- | --- |
| Orbit a body at a height | Right click a world, set "km up", ORBIT | Climbs out of its sky if it is in one, crosses to the body, matches a circular orbit there and shuts its drive |
| Go to a place | Right click empty space | Flies to that point (fixed against the body that rules there), stops and holds it |
| Fly to a spot over the ground | Right click the ground of the world it is flying over | The Flight Stabilizer's own waypoint autopilot, at the ship's present height |
| Land on a spot | Right click the ground of the world you are on, LAND HERE (from its sky or from space) | Comes down into the sky over the spot if it is in space, flies over it and sets down on it ("Landing", below) |
| Dock with another craft | Right click another craft, DOCK WITH <NAME> ("NO PORT AVAILABLE" when it has none free) | Flies to the point in front of a free docking port on it and in, until the port holds it ("Docking", below) |
| Rendezvous with another craft | Right click another craft, set "m apart", RENDEZVOUS WITH <NAME> | Flies to its side, matches its motion (its orbit, or its burn) and holds there alongside, drive parallel to it, until cancelled ("Rendezvous", below) |
| Come to me | Right click your own marker (YOU), COME TO ME, or COME TO ME on the card | You on the ground or in the sky: as a landing, on a spot beside you on the side it comes from, which follows you until it starts down. You out in space (over the sky's edge, on a spacewalk or another ship): it flies to you and holds beside you, matching your motion, until cancelled |

The lowest orbit (`Game.NavMinOrbit`) is `NavEdgeMargin` (400 m) over a mapped world's sky edge, since inside the sky a
ship is a physics body again; round a body with no map it is a little clear of the ground. The highest is 0.8 of the
body's sphere of influence.

**An order ends** when it is cancelled, when its ship is gone or stops taking orders, or when a pilot at its helm
touches the flight keys: hands on the controls always win. An orbit order stays after the ship is in orbit ("IN
ORBIT, DRIVE OFF") and burns again only if the ship has drifted well off its circle.

### Boarding (`Game/GameNavBoard.cs`)

Board, for a ship with a boarding pod docked on it (a craft with mag locks and a breaching cutter facing its nose: the Zero
Day's Limpet, [limpet.md](../sandbox/constructs/limpet.md)). Pick the carrier, then right-click another craft: the part
under the cursor (in its hologram; zoom in until it is large) is the spot, a door if one is under it, and the menu offers
BOARD: SEAL ON THIS DOOR or BOARD: CUT IN HERE under the Dock line. Zoomed out (or off its hull) it offers BOARD: CUT INTO
ITS HULL... and BOARD: SEAL ON A DOOR... instead (Karl, 2026-11-18): the view flies to that craft and pins on it, a banner
says what to click, the cursor's label names the door (or says CUT IN HERE), and a left click sends the order and puts the
view back as it was; a right click or Esc puts it back with no order (UI/UiCommandBoard.cs). The order goes on the pod (its card shows it): it flies out from under the
carrier, across, grips, then opens the door or cuts through and opens its own. On the wire its point is the target's id,
the part and 1 for a door (protocol 342); the host works the spot and the way out of the skin there. The pod must be within
3 km of the target. `order board N door|hull` (the window tool) gives it from a script.

**The close hologram and the cut-in preview** (UI/UiCommandHolo.cs, Karl 2026-11-18: "you cannot see where you're cutting").
The picked craft, or the craft being boarded, once it is over about 220 px across is cut again finer: one craft at a time,
cells of 0.25 m or more (200 across at most), a level every 0.9 m or more, at most 12,000 lines (bigger cells past that), built
once and again only when its parts change (about 5 ms and 2,900 lines for a Zero Day). Both holograms are cut from the parts'
own boxes and hull skin shapes, not their bounds, so a welded hull piece no longer fills the rooms it walls. On the fine one
the outer skin (lines beside cells open to the outside on that level) is bright, inner walls dim, outlines every other level;
doors and hatches are green boxes. While picking a hull spot, once the cursor rests on a part for 0.06 s the view works the
spot out as the host will (`Game.BoardPreview`: `BoardSpot`, then `BoardNearRoom`'s search, with no log lines; the search
costs up to a few hundred ms on a Zero Day, so it runs 2.5 ms a frame, one candidate at most past that, and each part's result
is kept while the pick lasts; there are two close-hologram slots, so the carrier and its target don't take turns rebuilding) and draws the hull piece
it cuts (amber box), the pod's skirt, the hole as `ConstructBreach.OpenHole` cuts it (1.1 by 2.1 m from the room's floor up,
red when no room is found), the tunnel in to the room's wall, and the room behind as an outline on its floor (the fine grid's
inside cells joined to where a person lands). The label says whether the room's floor is level with the pod's. Close up, a
part's middle within 2 m of the cursor counts as under it (12 to 48 px). Window tool: `cmdboard N door|hull` starts the
pick, `mouse X,Y` rests the cursor.

### How a ship flies one (`Game.ServerTickNav`, before the launch step each tick)

- **In a sky** (`NavInSky`): the order works the Flight Stabilizer's autopilot through `ConstructFlight.NavEngage`, as
  a player setting its panel would. A ground order is its Waypoint mode. An orbit or go-to order from the ground is its
  Hover mode with the target height `NavClimbOver` (600 m) over the sky's edge: the ship climbs out on its lift
  (phase `Climb`) at up to `NavClimbSpeed` (60 m/s, `Construct.NavClimb`: nobody is aboard to mind, and at the
  stabilizer's own limit Glacier's sky took four minutes), and the launch code makes it a space ship at the edge. There
  the autopilot is let go (`ConstructFlight.NavRelease`). A cancelled order leaves a ship in a sky hovering where it is.
  The autopilot flies a stabilizer on Ship keys too (it used only to stop one): to the height it holds and over the
  ground to its point.
- **In space** (`NavInSpace`): the order asks for an acceleration and the ship is turned to give it. The ask comes from
  `NavGuidance`: `Orbit` inside 0.9 of the target's sphere of influence (a velocity field: circular speed along the
  track plus a climb or sink towards the circle, with gravity's unused pull fed forward), else `GoTo` (a speed that
  falls with distance so the ship can stop with `BrakeShare`, 35%, of its thrust, the braking fed forward, and the
  local gravity cancelled). `Avoid` bends the way round every body's keep-out ball.
- **Turning and burning:** `ConstructPhysics.NavDrive` picks the stronger of the ship's forward and lift thruster
  groups; `Game.NavTurn` turns the hull so that group points along the ask at `NavTurnRate` (0.6 rad/s), in place of
  the pilot's keys in `SpacePose`; the throttle (`Construct.NavThrottle`, read by `SpaceThrust`) opens as the drive
  comes within `NavAlignNone`..`NavAlignFull` of the aim. Fuel is burnt as for a pilot's burn, and a burn holds the
  time speed at 1x as any burn does. Under an order `ConstructFlight.FliesShip` is false: the nav computer outranks a
  stabilizer on Ship keys, whose jets would otherwise turn the hull.

Phases (`NavPhase`): Climb, Transfer, Insert, Orbiting, Hold, Flying, Arrived, NoDrive (no fuel or no thruster that
works in a vacuum: the card says so in red); between planets Wait, Depart, Coast, Correct, Brake and Short (below);
for a landing Descend and Landing.

### Landing (`Game/GameNavLand.cs`)

A Land order's point is a spot on the ground of the bubble's world (World coordinates, on the ball's radius: only the
way to it along the ground counts); a Come order's spot is worked out on the host from the player who gave it
(`NavBeside`: the ship's own radius plus `NavComeBeside`, 8 m, from them, on the side it comes from), again each tick
until it starts down (or until they come aboard it: a player riding it can't order it to come to them). A plane or a
rocket refuses both (they land on their own), and so does a ship in another system or one too heavy for the world's
landing limit.

- **From space** the order's `GoTo` law aims at a point `NavDescendUnder` (300 m) under the sky's edge over the spot
  (`NavSkyOver`, turning with the ground; on a thin sky half way down it), with nothing left of its speed there, so it
  crosses the line where it is a physics body again (200 m under the edge) slowly (phase `Descend`; time runs at 1x).
  A landing on another planet first flies the trip there.
- **In the sky** the autopilot's Waypoint mode flies it over the spot at its present height (`Flying`; off the ground
  first if it sits there, at the stabilizer's own climb limit). Within `NavLandNear` (20 m) and drifting under 4 m/s it
  comes down (`Landing`): `Construct.NavLand` (`NavLandSpeed`, 40 m/s) takes over the stabilizer's height loop, which
  looks down `NavLandLook` (600 m) every 6 ticks and comes down at a fifth of the gap a second, and no faster than half
  its spare lift could stop in the gap (0.6 m/s at the least), until the gap is gone.
- **Come to a player in space** (`NavBesideInSpace`): when its player is over the sky's edge (`NavPlayerInSpace`; they
  count as back in the sky 150 m under it, so the line doesn't flip the order), the ship climbs out of its sky if it is
  in one and flies the `GoTo` law in the player's own frame: to its radius plus `NavComeBeside` plus `NavComeStandoff`
  (40 m) from them, on the side it comes from, planned on no more than `NavComeAccel` (5 m/s2) since it turns to brake
  and an overshoot would run them down; only the difference in gravity between it and them is cancelled. It holds there
  (phase Hold) as they drift; a player just under the edge has it hold over them, so it stays out of the sky. A player
  who rides it, dies or leaves ends it ("You are aboard it" when the order is given). A player who goes back down has it
  land beside them, if it can land there (the plane, rocket and weight checks are made again then, `NavCantLand`).
- **Down:** it has sat still while asked down for a second within 4 m of the ground: the order ends, its autopilot off and its stabilizer at rest (`ConstructFlight.NavSetDown`), as a pilot leaves
  a ship that landed. Still for ten seconds higher up (a repulsor field or its gas holding it, or something the ray
  doesn't see) the order gives up and leaves it hovering, since let go in the air it would fall. Cancelling a landing
  leaves it hovering too, as any order in a sky does.

### Docking (`Game/GameNavDock.cs`)

Karl, 2026-11-17: "select a ship, right click on another with the option to dock. If there is no docking port available,
it will show 'no port available'". (He asked for the admiral's orders too: the admirals are only designed so far,
docs/sandbox/design/faction-controllers.md section 6.2, so he chose docking alone for now.)

- **A docking port** is any part with a `"dock"` (Docking Clamp, Medium, Large and Capital, Magnetic Lock and Medium,
  Hangar Bay, Medium and Large) that is working, holds no other craft and takes the ship's mass (`DockMaxMass`):
  `Game.NavDockFor`, the same on both peers, so the menu says "NO PORT AVAILABLE" when the host would refuse. Armed
  ports come first, then the nearest. The host checks the port again once a second and moves to another free one if
  it was taken; none left ends the order.
- **The way in** (`NavDockCentre`): a clamp or a lock is come at from over it (its up), a hangar bay through its mouth
  (where it ejects). The ship lines up `NavDockOver` (30 m) off the port's box (phase Flying, "LINING UP WITH THE PORT"),
  then closes on the box's middle at `NavDockSink` (2 m/s, Docking). An unarmed or unpowered port has it wait there
  (DockWait, "WAITING: THE PORT ISN'T ARMED OR HAS NO POWER"); after a minute at the port without being taken the order
  gives up and says why. A station's berths are come at down their approach lanes instead (docs/sandbox/constructs/station.md
  "Approach lanes"): round the station to the lane's mouth, then straight in.
- **In a sky** the stabilizer's autopilot flies it (Waypoint mode, written only when the point has moved a metre), and
  the port takes it as it takes any craft: a clamp's tractor from its pull's reach, a bay or a lock once it is in the box
  and slow. While it lines up no tractor takes it (`Construct.DockAway` held a tick ahead): a Shuttle drawn in by the
  Zero Day's clamp from beside and below was dragged up the frigate's side and turned over. Once the tractor has it
  the autopilot only holds it level and on its heading (Heading mode), since a position hold keeps it 2 m off the middle
  the clamp snaps to, and let go altogether a craft stops levelling and can tip onto its back. A craft on repulsors holds
  its own height whatever the autopilot asks, so it lines up over the ground only and the clamp's pull does the rest.
- **In space** both ships are particles and their physics sleeps, so no tractor works: the nav computer flies the
  `GoTo` law in the other ship's moving frame (at no more than `NavComeAccel`), and once the ship is in the port's
  reach (a pull dock's: half the stand-off; a bay's or lock's: in its box) and under `NavDockSlow` (3 m/s) against it,
  the host docks it (`NavDockCatch`): out of the flight, set upright in the middle of a pull dock's box and caught
  (`ConstructCargo.Catch`), so it rides the other ship as any docked craft does. A ship in a sky climbs out first to dock
  with one in space; one in space is refused a port down in a sky ("bring it down into the sky first").
- **Close work in space** (`NavDockRcs`, 2026-11-18): within `NavDockRcsNear` (20 m) of the point it aims at and under
  `NavDockRcsSlip` (4 m/s) against it, and all the way in once lined up, the ship no longer turns its main drive to each
  correction (it twirled at its full turn rate over the port and swept its hull through the other ship): its speed against
  the port is steered straight (its flight's velocity changed, at most `NavDockRcsAccel`, 1.5 m/s², as its small jets would,
  lit for show by `ThrustPuppet`), and it turns upright with the port. No port's tractor works out of the bubble
  (`ConstructCarrier.Tractor`: both ships are posed by their flights, so its pull was undone every tick while its spin and its
  push back on the carrier stayed, and both ships tumbled), and a ship a port catches there by its box (`ConstructCargo.Scan`)
  leaves the flight and rides the carrier (`SpaceLaunchAfter`); let go, it leaves the bubble again as a ship.
- **Wire** (protocol 342): `CActKind.NavOrder` ends with the target construct, `Msg.Nav` with the target and the port.
- **Trying it:** `order dock N` (dock with the Nth craft, as `cmdpick` counts) and `letgo [ID]` (host: the port holding
  a craft lets it go). A Shuttle onto the Zero Day's clamp, in the sky and in orbit:

```
--window-shot out.png --map Glacier --blueprint "Zero Day Frigate" --calm --do "command 3000; spawnbp Shuttle; wait 120; letgo 2; cmdpick 2; hoist 1; wait 30; cmdpick 3; order dock 1; wait 2400; nav"
--window-shot out.png --map Glacier --blueprint "Zero Day Frigate" --calm --do "command 60000; cmdpick 1; order orbit Glacier 5.5; wait 8000; letgo 2; wait 400; cmdpick 2; order dock 1; wait 3600; nav"
```

- **A station** (Caledon Highport) is drawn as a station, not a ship, and its berths moor a ship rather than carry it, so a
  ship carrying craft (the Zero Day) docks there too: docs/sandbox/constructs/station.md "In the game".

### Rendezvous (`Game/GameNavMeet.cs`)

Karl, 2026-11-18: "one ship to match another ship's velocity or orbit and meet up alongside". `NavKind.Meet`: the order's
`Target` is the other craft and its `Radius` the gap kept between the hulls (the menu's "m apart", 10 to 2000 m, 40 by
default; `NavMeetGap`).

- **Where it holds:** on the other craft's side (its own X axis, on the side the ship comes from, chosen once), both
  their radii plus the gap from its middle. It follows that side as the other turns.
- **In space** it flies the `GoTo` law in the other craft's moving frame: offset and velocity from it, `Avoid` round it
  (coming from its far side the straight line runs through it) and round the world it is near, and the other's own
  acceleration fed forward (measured from its velocity tick to tick: its gravity, and a burn of its own), so a ship in orbit
  is matched without the drive fighting gravity. Far off the approach is planned on the whole drive, closer in it falls
  towards `NavComeAccel` (5 m/s2) with distance (`NavMeetFar`, 20 km), since it turns to brake and an overshoot would hit
  it. Within 30 m (or a quarter of the gap) and 1.5 m/s it is Hold ("ALONGSIDE, MATCHING ITS MOTION"): the drive rests
  and the hull turns its drive parallel to the other's (that craft's strongest drive axis, looked for once a second).
  Time is held at 1x while it holds, as any Hold is.
- **Round another planet:** the order's world is the one the other craft is round now (`NavMeetBody`); a ship outside
  that planet's reach flies the trip there first (planned, waited for, coasted, as an orbit's), and its own law takes over
  when the trip hands it on.
- **In a sky** (both bodies there) the stabilizer's autopilot flies it (Waypoint mode) to the point beside the other at
  the other's height, written at most twice a second and only when it has moved 6 m. A ship in a sky climbs out to meet a
  craft in space; one in space is refused a craft down in a sky ("bring it down into the sky first").
- **It ends** when cancelled, the other craft is gone or wrecked, goes down into a sky from space, or the two dock.
- **Wire** (protocol 350): `NavKind.Meet` at its end; the target and gap ride the order's existing target and radius.
- **Trying it:** `order meet N [GAP]` (the Nth craft, as `cmdpick` counts). The Shuttle from a 9 km orbit to the Zero
  Day's at 5.5 km (about 75 s to alongside):

```
--window-shot out.png --map Glacier --blueprint "Zero Day Frigate" --calm --do "command 60000; cmdpick 1; order orbit Glacier 5.5; wait 8000; letgo 2; wait 200; cmdpick 2; order orbit Glacier 9; wait 6000; order meet 1 60; wait 6000; nav"
```

## Time speed

Karl, 2026-11-16: ships coast between planets in real time, and the player speeds time up from the command view. (It
was the "time warp" of the F7 Space window until then; warp no longer means changing time: docs/archive/space/TIME-WARP.md.)

- **The buttons** (host only) sit under the clock, top left: II (pause), 1x, 10x, 100x, 1K, 10K, 100K, 1M
  (`Game.RequestTimeSpeed`). A joined player sees the clock and its speed, not the buttons. A button asks for its speed
  whatever the settings say; the settings' limit holds the clock and the clock line says so.
- **What holds it lower** (the clock line says which): any ship in space burning (`SpaceTimeRule`: "A SHIP IS
  BURNING"); a ship under orders about to burn ("SLOWED FOR A BURN": `Game.NavSpeedCap`, see below); the ships' own
  step budget (`SpaceShips.MaxSpeed`: also a ship on rails nearing a sphere of influence, or a coasting ship whose orbit
  dips under a world's sky, `SpaceShips.Sky`); the host's Settings > Gameplay "Fastest time speed".
- **Over 10K ships go on rails** (2026-11-17, `SpaceTimeRule.WarpRailsAbove`): a coasting ship whose other bodies' pull
  is under 2% of its primary's flies its own two-body orbit (the patched conic, as space games do at high warp), exact
  and as cheap at 1M as at 1x, so a moon's tide no longer holds time near 15,000x and the drawn orbit stands still. At
  10K and under they fly the full model as before. Notes: docs/space/warp-notes.md.
- Life in a bubble (walking, building, a ship in a sky) runs at 60 Hz whatever the clock does; only the sky, the
  orbits and ships in space go faster.

## Between planets

An orbit or go-to order whose target is round another planet (the ship is outside the sphere of influence of the
target's planet: `NavTrip.Crosses`) is a **trip**: the transfer conics Hohmann flew, quicker as fuel allows (Karl,
2026-11-16: "flip and burn level of realism", "faster than Hohmann but has the same traits"). Frostreach is real-sized:
its planets are 0.35 to 4 AU from a star of 1.8 suns, and they go round at 20 to 67 km/s. A Magpie (4.3 g, 26 km/s of dV
at full fuel use) reaches Mille from Glacier in about 400 days of game time on half its tank; at 100K that is about six
minutes, at 1M about 35 seconds. Cinder, deep in the star's well, is out of its reach (36 km/s at the least): a ship with more dV, or the "Fuel
use in space" setting, gets there.

1. **Plan** (`NavTrip.Search`, a few departures a tick: `Game.NavPlanPerTick`, time held at 1x for the second it
   takes). For each departure time from now to just past the next launch window (`NavTrip.WaitReach` of the two
   planets' synodic period, at most four years, 160 times), `NavTrip.Plan` scans trip times from the quickest flip and
   burn could do (no coast) to four years and takes the quickest whose two burns fit the dV a trip may spend (Trip fuel,
   `Game.TripShare`, of what its fuel buys: `Game.NavDvLeft`) and last at most a fifth of the trip. Each is a
   `NavTrip.Lambert` solve round the star (universal variables, short or long way, the cheaper). The plan is the
   departure that **arrives soonest**. None fits: phase Short, the card says the least any departure needs, and it plans
   again every ten seconds.
2. **Wait** for the window in orbit, drive off (`Game.NavPark`); time may run fast, but is held so the departure comes
   in no less than `NavLeadSeconds` (1.5 s) of real time. Bodies are on rails, so a moon doesn't tug its planet and
   its full pull wobbles a low orbit's shape by kilometres over the days (Slopolis round Glacier): the orbit may
   wander, and is trimmed towards a circle (at 1x, seconds) only when its low point nears the sky's edge (under which
   the ship would be a physics body in the bubble again) or its high point nears the edge of the world's reach.
   Round a small world time ran at what the ship's steps allowed (about 15,000x round Glacier) until ships went on rails
   above 10K (2026-11-17): a parked orbit now runs at the full 1M, and on rails it doesn't wander.
3. **Depart**: the burn out, closed-loop, re-solved every tick for the planned arrival from where the ship really is
   (minutes at 4 g), bent round any body in the way (`NavGuidance.Avoid`).
4. **Coast** round the star with the drive off; every half second (`NavCoastCheck`) it solves again and burns back onto
   the plan (**Correct**) when it has drifted by more than 3 m/s or 0.3% of its speed past the target.
5. **Brake**: once it is as close as its drive needs to stop from the speed it closes at (with a quarter's margin and
   twice the target's lowest orbit), the trip hands it to the order's own law (`NavGuidance.Orbit` or `GoTo`), which
   flips it and brakes into the orbit or onto the point. While it coasts towards that point the time speed is held so it
   gets there in no less than 1.5 s of real time (`Game.NavSpeedCap`); from the hand-over until the orbit has settled
   (Insert, Brake, Transfer and Hold) time is held at 1x, since an insertion's push dips between corrections and one
   sped-up tick (half an hour of space at 100K) flings the ship out of the planet's reach. The orbit law climbs at no
   more than a fifth of the circle's speed (`NavGuidance.Orbit`: a strong drive under its circle once asked for more
   than escape speed round a small world).

The card's TRIP row says when it leaves, when it is there and the dV the trip spends. The drawn conic is its transfer.
A trip made on a mid-course plan keeps its arrival time: a ship whose fuel or thrust changes on the way may need more
than the plan said.

**Settings > Gameplay** (the host's, Karl 2026-11-16: "a gameplay slider ... where we can adjust fuel, speed scaling"):

| Setting | Default | What it does |
| --- | --- | --- |
| Trip fuel | 50% | The share of a ship's dV one trip may spend (10 to 95%; `Game.TripShare`). More is quicker; less leaves fuel for the way home |
| Fuel use in space | 100% | The share of their fuel thrusters burn in space (10 to 100%; `ConstructPhysics.SpaceFuelScale`, sent to clients with the space clock). At 25% a Magpie carries about 100 km/s and reaches any planet in 120 to 170 days |
| Fastest time speed | 1000000x | The fastest the command view's time speed runs (`Game.TimeSpeedMost`) |

`--trip-check [FILE] [--accel A] [--dv DV] [--share S] [--day D]` prints the trip an order would plan between every pair
of planets (default Frostreach, a Magpie leaving on day 0): when it leaves, how long it takes and its dV.

**Jump drives** (Karl, 2026-11-16: an option, but most ships don't carry one and using it is costly, the cost to be
decided). The Jump Drive only crosses between star systems today (docs/space/GALAXY.md). A jump within a system is not
built: `Game.NavJumpCost` (GameNavTrip.cs) is where its price goes, and returns none for every ship, so the order menu
offers no jump. When the cost is decided: what pays it (power, a fuel, wear on the drive) goes there, the menu offers
JUMP for a ship with a working drive when it gives a cost, and the jump arrives as `Galaxy.Arrive` does, in orbit round
the target.

### On the wire (protocols 323, 326 and 327)

- Client to host: `Msg.CAct` with `CActKind.NavOrder`: construct, kind, body, point (three doubles), radius. The host
  checks it (`NavOrderAct`) and answers a refusal in words.
- Host to all: `Msg.Nav`, the whole order list (construct, kind, body, point, radius, phase, and a trip's arrival and
  departure in space ns and its dV: 326), sent when an order starts, changes phase or its plan, or ends, and to each
  joiner (`SendNavState`). Clients draw from it; they fly nothing.
- 327: `NavKind` Land and Come, `NavPhase` Descend and Landing. A Come order's point on the wire is where the host first
  put it; the view draws its mark on the player while it follows them.
- The space clock (`SpaceMsg.Clock`) carries the host's Fuel use in space (a u8 percent, 326), so a client's card works
  out the same dV.

## Whose ships you may order

(Karl, 2026-11-18; `Game/GameShipControl.cs`.) A player gives orders only to a ship that is theirs, a faction mate's, or nobody's.
A ship crewed by an NPC power (the Traders' Zero Day in the Zero Day start, any power's crewed ship), and a nobody's craft docked in
or carried by one (its Shuttle), is that power's: it can be picked, read on its card (OWNER TRADERS), tracked, locked onto and
fought, but the order menu and the card's buttons say "IT IS THE TRADERS' SHIP", and the host refuses its orders, a cancel
included (`Game.CommandRefusal` in `NavOrderAct`). Another player's ship outside your faction is refused the same way. Its helm and
the seats its crew fly and fight from can't be sat in, and its drones and robot shells can't be linked to.
Boarded and taken (its crew surrendered: the Traders give up the moment anyone comes aboard), it is the captor's, with every
nobody's craft it carries (`GameCrewBoard.CrewCaptureCraft`).

## What it doesn't do yet

- **A jump within a system** (above, "Jump drives"): its cost is Karl's to decide.
- A trip's plan isn't shown before the order is given (its time and dV are on the card once it is), and the conic
  is drawn only for the ship's present orbit.
- Holding a place near a body burns fuel all the while (the ship hovers on its drive). A ship with no pilot and an
  empty tank falls.
- Orders aren't saved, there are no queues or patrols, and one ship is ordered at a time (no box select).

## Trying it

The window tool (README.md "Testing without playing") has verbs for it (`Source/App/Program.WindowShotNav.cs`):
`command [SPAN]` opens the view (SPAN: metres up the screen), `cmdview YAW PITCH` turns it (degrees), `cmdpick N` picks
the Nth craft, `order orbit BODY KM`, `order goto BODY KM`, `order ground M` (a spot M metres along the ground),
`order land M` (a spot M metres along the ground to set down on; from space, along from under the ship), `order come`,
`spacewalk KM [SIDE]` (host: you out KM km over the ground under the picked craft, SIDE m beside it, at rest) and
`order cancel` give the picked craft an order as the menu would; `linked layer [on|off]` (Program.WindowShotLinked.cs) turns the logistics layer on or off and prints its factories, links and legs, `missiles [on|off]` the missiles layer (and how many it drew), `missile M [SPEED]` (host) a test missile M metres off the picked craft, locked on it, (an orbit of another planet is a trip), `rclick X,Y` is a right click (the menu itself), `nav` prints every craft and its order, `hoist KM` (host) puts
the picked craft that high, at rest, `speed N` asks for a time speed (host), and `travel FUEL SHARE` sets Settings >
Gameplay's Fuel use in space and Trip fuel for the run (percents). The `nav` line has each craft's acceleration and dV. For example, a Magpie into orbit and on to the
moon:

```
--window-shot out.png --map Glacier --blueprint "Magpie Mining Ship" --calm --do "command 60000; order orbit Glacier 5.5; wait 7000; nav; shot orbit; order orbit Rime 2; wait 12000; nav"
```

A trip, Glacier to Mille, at a quarter of the fuel (about 100 km/s) and time at 100K:

```
--window-shot out.png --map Glacier --blueprint "Magpie Mining Ship" --calm --do "command 60000; cmdpick 1; hoist 15; wait 60; travel 25 50; order orbit Mille 5; wait 60; speed 100000; wait 2000; nav"
```
