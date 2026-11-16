# The command view and ship orders

The command view is the game's map: the star system drawn as a hologram over the dimmed game, on one wheel from a ship's
decks out to every planet's orbit. A player picks a ship in it, reads what the ship is doing, and tells it where to go; a
ship with a working flight computer then flies there by itself. It is the first piece of the RTS side of Survival (Karl,
2026-11-16): moving ships from A to B without sitting in them.

- **Key:** Home (`GameAction.CommandView`, rebindable; M is the Mirror Table's). Esc closes the order menu, then the view.
  Sandbox only, on a map that is in a star system (`Game.Space`); elsewhere the key says why not.
- **Code:** the picture `Source/UI/UiCommand.cs`, the ships in it and the ship card `UiCommandShip.cs`, the order menu
  `UiCommandOrders.cs`, the key `Source/App/Program.Command.cs`; the orders `Source/Game/GameNav.cs`; the steering maths
  `Source/Space/NavGuidance.cs`.
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
| Right click | An order for the picked ship: on a world, an orbit; on a place in space, go there |
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
| CARGO, CREW | What its holds carry, who is aboard |
| SYSTEMS | Flight computer (and whether its autopilot is on), jump drive, guns and thrusters, counted |
| ORDER, NOW | Its order and what it is doing about it; why it takes none ("NO FLIGHT COMPUTER") |

Buttons: CANCEL ORDER, a one-click "ORBIT <BODY> LOW", CENTRE. A picked body has a small card of its own (what it is a
planet or moon of, radius, gravity at the ground, how far out it is and its time round, whether it can be landed on).

## Orders

Three kinds (`NavKind`), each for one ship, given by any player to any ship that takes orders. **A ship takes orders
when it has a working Flight Stabilizer** (the "flight computer"; `Game.NavProblem` says why not: a wreck, none fitted,
wrecked, or held down). Orders are the host's (`Game.NavOrders`); they are not saved and end when the map changes.

| Order | From the menu | What the ship does |
| --- | --- | --- |
| Orbit a body at a height | Right click a world, set "km up", ORBIT | Climbs out of its sky if it is in one, crosses to the body, matches a circular orbit there and shuts its drive |
| Go to a place | Right click empty space | Flies to that point (fixed against the body that rules there), stops and holds it |
| Fly to a spot over the ground | Right click the ground of the world it is flying over | The Flight Stabilizer's own waypoint autopilot, at the ship's present height |

The lowest orbit (`Game.NavMinOrbit`) is `NavEdgeMargin` (400 m) over a mapped world's sky edge, since inside the sky a
ship is a physics body again; round a body with no map it is a little clear of the ground. The highest is 0.8 of the
body's sphere of influence.

**An order ends** when it is cancelled, when its ship is gone or stops taking orders, or when a pilot at its helm
touches the flight keys: hands on the controls always win. An orbit order stays after the ship is in orbit ("IN
ORBIT, DRIVE OFF") and burns again only if the ship has drifted well off its circle.

### How a ship flies one (`Game.ServerTickNav`, before the launch step each tick)

- **In a sky** (`NavInSky`): the order works the Flight Stabilizer's autopilot through `ConstructFlight.NavEngage`, as
  a player setting its panel would. A ground order is its Waypoint mode. An orbit or go-to order from the ground is its
  Hover mode with the target height `NavClimbOver` (600 m) over the sky's edge: the ship climbs out on its lift
  (phase `Climb`) and the launch code makes it a space ship at the edge.
- **In space** (`NavInSpace`): the order asks for an acceleration and the ship is turned to give it. The ask comes from
  `NavGuidance`: `Orbit` inside 0.9 of the target's sphere of influence (a velocity field: circular speed along the
  track plus a climb or sink towards the circle, with gravity's unused pull fed forward), else `GoTo` (a speed that
  falls with distance so the ship can stop with `BrakeShare`, 35%, of its thrust, the braking fed forward, and the
  local gravity cancelled). `Avoid` bends the way round every body's keep-out ball.
- **Turning and burning:** `ConstructPhysics.NavDrive` picks the stronger of the ship's forward and lift thruster
  groups; `Game.NavTurn` turns the hull so that group points along the ask at `NavTurnRate` (0.6 rad/s), in place of
  the pilot's keys in `SpacePose`; the throttle (`Construct.NavThrottle`, read by `SpaceThrust`) opens as the drive
  comes within `NavAlignNone`..`NavAlignFull` of the aim. Fuel is burnt as for a pilot's burn, and a burn holds the
  time warp at 1x as any burn does.

Phases (`NavPhase`): Climb, Transfer, Insert, Orbiting, Hold, Flying, Arrived, NoDrive (no fuel or no thruster that
works in a vacuum: the card says so in red).

### On the wire (protocol 319)

- Client to host: `Msg.CAct` with `CActKind.NavOrder`: construct, kind, body, point (three doubles), radius. The host
  checks it (`NavOrderAct`) and answers a refusal in words.
- Host to all: `Msg.Nav`, the whole order list (construct, kind, body, point, radius, phase), sent when an order
  starts, changes phase or ends, and to each joiner (`SendNavState`). Clients draw from it; they fly nothing.

## What it doesn't do yet

- **Between planets.** The planets are AU apart, and a burn holds warp at 1x, so an order to another planet would take
  many hours of play. Orders work now within a planet's neighbourhood (its sky, its orbit, its moons: Glacier to Rime is
  about three minutes). How ships cross between planets (warp under orders, a jump, or timed coasting) is undecided.
- Holding a place near a body burns fuel all the while (the ship hovers on its drive). A ship with no pilot and an
  empty tank falls.
- Orders aren't saved, there are no queues or patrols, and one ship is ordered at a time (no box select).
- Anyone may order any ship that takes orders: there is no ownership check yet.

## Trying it

The window tool (README.md "Testing without playing") has verbs for it (`Source/App/Program.WindowShotNav.cs`):
`command [SPAN]` opens the view (SPAN: metres up the screen), `cmdview YAW PITCH` turns it (degrees), `cmdpick N` picks
the Nth craft, `order orbit BODY KM`, `order goto BODY KM` and `order cancel` give the picked craft an order as the menu
would, `rclick X,Y` is a right click (the menu itself), `nav` prints every craft and its order, `hoist KM` (host) puts
the picked craft that high, at rest, and `warp N` asks for a time warp. For example, a Magpie into orbit and on to the
moon:

```
--window-shot out.png --map Glacier --blueprint "Magpie Mining Ship" --calm --do "command 60000; hoist 2.9; order orbit Glacier 5.5; wait 3000; nav; shot orbit; order orbit Rime 2; wait 12000; nav"
```
