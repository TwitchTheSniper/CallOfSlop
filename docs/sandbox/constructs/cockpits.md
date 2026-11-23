# Modular cockpits

Karl, 2026-11-22: buttons, screens, gauges, levers and lamps mount on consoles and on the hull with the same mounting system as the in-wall bays (module sockets with their styles), and a cockpit is linked to its seat, so a seat's state (dampeners off and so on) shows on the dash. The storyboard is the Artifact https://claude.ai/artifact/R99CRAFv1fxTaH9MGc6w1m and the plan the Claude Doc https://claude.ai/artifact/HxJ53EmqN1uUDpkPtdBNxS. Phases 1 to 3 are built, and of the rest phase 4 (command stations) and phase 6 (Console Drawers, curved desks). Phase 5 was first console slots and command rooms in the procedural ship generator; Karl dropped that generator on 2026-11-22 and phase 5 is now command rooms by ship size and faction, built into each shipped ship's own generator (plan: the Claude Doc https://claude.ai/artifact/DnhHNWv8mSpQ4H7KepLFkL; see "Command rooms by ship size (phase 5)" below).

**The captain (Karl, 2026-11-22):** crews are coming and the player as captain coordinates them. The captain's station gives crew orders and repeats other stations' pages read only; it does not work their controls. To work a station, sit at it.

## Console bays: the cell class of module socket

A **Console Bay W x L** is a Module Socket whose footprint counts 10 cm cells, not metres (`"moduleSocket": { "w": W, "l": L, "cells": true }`; `ConstructPartDef.ModCells`, `CModSocket.Cells`, `ModuleSockets.Cell` = 0.1 m, at most `MostCells` = 40 a side). Everything else is the module socket standard (`docs/sandbox/design/module-sockets.md`): the kinds, the styles (Proud, Flush, Well, Opening), depth and room settings (5 cm steps, `ModuleSocketTable.CellStep`; room 0.3 m by default), keying, the data pass-throughs, the room checks and the dug well. Its **Console Plug W x L** is the faceplate: a Small socket on each 10 cm cell (every other cell when that would pass a part's 64 sockets), so instruments snap to the grid, and the attach tool's socket weld puts them anywhere else on it. A console is fitted and taken off by hand (F; `ModuleSocketTable.HandSize` is true for every cell size), never by a Module Fitter, has no side controls, and is never a candidate for faction module choice (`ModuleChoice` skips cell sockets and cell designs). A module blueprint of one says `"cells": true` in its `"module"` block.

The part kit's `cockpit_parts` (`Source/PartKit/Generators/CockpitParts.cs`, folder `Constructs/Parts/Cockpit`, inventory category Cockpit) writes fourteen sizes: 1x4, 1x10, 2x2, 2x4, 3x3, 4x4, 4x6, 6x6, 6x10 and the Roamer dash's 1x30, 3x6, 5x12, 5x8, 4x10. Every solid box is at least 1 cm on each side (the kit drops a thinner one), so a bay is 3.5 cm deep and a faceplate 2.6 cm.

## Panel instruments

`ControlParts.PanelInstruments` (written into the cockpit folder, section "Cockpit instruments"): the hand controls and screens cut down to the cell grid. Toggle Switch, Rocker Switch, Push Button, Rotary Knob and Slider at half the small size (a 10 cm housing, one cell); Throttle Lever at 0.7; Number Readout, Bar Gauge, Tall Bar Gauge, Dial Gauge, Graph Screen, Text Screen, Compass, Altimeter, Speedometer, Lamp Strips 4 and 8 and a Wide Screen at 0.4 (a readout two cells by one, a dial two by two). They are the same logic kinds as their full-size parts.

## The seat link: Function

A panel switch or button has a **Function** setting (`CfgKey.SeatFn`, the old unused Blade5 slot; `CockpitFns.cs`): None, Dampeners, Mouse steering, Reactor, Thrusters, Landing legs, Master caution. With a Function:

- **It shows the craft's state.** The logic tick (server) sets its output each tick from the same state the seat panel shows (`CockpitFns.Shown`), so the bat flips when the seat's key turns the dampeners off, and anything wired from it (a Lamp Strip, a light) follows.
- **A press works it** through the same toggles as the seat's keys (`CockpitFns.Press`: `ConstructFlight.ToggleDampeners`, `ToggleMouseSteer`, `ConstructShutdown.ToggleReactor`, `ToggleThrusters`, `ConstructDoors.ToggleGear`).
- **MASTER CAUTION** is lit while a caution is on that wasn't acknowledged (dampeners off, the reactor down, the thrusters down: `CockpitFns.Cautions`); a press acknowledges them (kept in the part's `Count`), and a new caution lights it again.

The state is the craft's: these are ship-wide states, so a switch on any console of the craft shows them. It is worked only from a seat of that craft ("sit at the console to work it"), and Reactor and Thrusters only from the pilot's seat (the drive seat), as the seat's own keys: a passenger or a boarder can't shut the ship down from the dash. Seat-specific links (a station's own pages, crew stations) are phase 4.

## Working it from the seat

Seated, the use key (F) on a switch or button in reach of the seat presses it (`Program.SwitchSeated`, `CActKind.Use` as when standing); the look line names what it works and its state. Only a switch with a Function takes F: F on a plain switch, or on nothing in reach, still gets up. (The plan doc asks Karl whether a mouse click should press too.)

## The Roamer's dash

Three bays across the desk top (`RoamerParts.DashBays`, the blueprint's `Dash`): the pilot's 5x12 (speedometer, altimeter, compass; Dampeners and Mouse steering toggles, Reactor and Thrusters rockers), the centre 5x8 (MASTER CAUTION, Landing legs) and the co-pilot's 5x12 (the same three instruments, MASTER CAUTION and Landing legs), each turned with its long side across the ship and its switches on the seat's side. The desk reaches forward of the cockpit's front wall, so that hidden wall starts above the bays' room over the desk (`DashWallFoot`); `--check-building Roamer` reaches every switch.

Tried: `--window-shot` seated in the pilot's couch, F on the Dampeners switch turned the dampeners off (the seat panel's lamp and the switch both went off). Not tried: a joined client, the Function on a switch a player builds by hand, Flush or Well console bays dug into a dash.

## Command stations (phase 4)

Every seat part with no setting of its own is given a **Station** setting (`CockpitStations.cs`; its `CPart.Setting`, so it saves, travels with the body and the linking tool's reload key cycles it): Auto, Command, Navigation, Sensors, Damage control, Comms. Auto is as before: the drive seat is the helm, a seat that works a gun the gun seat, any other a couch.

- **Crews man them.** `CrewWorld.Scan` makes a station seat a crew station of its kind (`CrewStationKind.Command` to `Comms`, jobs `Officer`, `Navigator`, `Sensors`, `DamageControl`, `Signals`), filled after the helm (Command) and after the guns and works (the rest), before the couches. Its member sits in it as at the helm. They do nothing more yet: what a navigator or a sensors hand does is for the crew work to come.
- **The command seat** gives crew orders: the first seat set to Command, or the drive seat on a craft with none (`CockpitStations.Commands`). A seat set to Command shows in its seat panel COMMAND (the crew holding fire or not), STATIONS (every station seat, up to ten, and whether a person, a crew body or a stand-in mans it; looked at four times a second) and, away from the helm, HELM read only (dampeners, mouse steering, reactor, thrusters, legs, as lamps with no keys). The captain orders and watches; to work a station's controls, sit at it. The status line at a station seat names it ("Command station").
- **Crew orders from the dash.** A switch's Function "Crew hold fire" shows whether the craft's own crew holds fire and a press gives the order (the same as the Crew window's Hold fire / Fire at will); it is worked only from the command seat ("crew orders are given from the command seat").

## Console Drawers (phase 6: pull-outs and pop-ups)

A **Console Drawer Case 2xL** (L 2, 4, 6, 8) is the module of a 2xL Console Bay set to **Opening**: a sleeve back into the hole behind the bay's face (set the bay's Depth to at least the tray's depth plus 5 cm). A **Console Drawer Tray 2xL** hangs on the case (its `tray` socket) and is an event door that slides out along the socket's normal (`CockpitParts.DrawerTravel`: D cells and 1.5 cm). Its deck has (L-1) x D Small sockets for panel instruments (D 2, 3, 3, 4), and each instrument on the tray alone rides it (`Construct.MaxDoorLeaf`, now 32). In a wall it pulls out like a keyboard tray, in a desk it rises like a pop-up screen.

- F on the tray slides it out and back, standing or from a seat in reach (`Program.DrawerSeated`); F on an instrument on it works the instrument.
- **Pop-up when the seat is taken:** wire the seat to the tray (a seat's output is on while someone sits), and the door's input runs it out and back.
- An instrument is a whole cell: a Dial Gauge (two cells) doesn't fit a drawer.

## Console Arc Desks (phase 6: curved consoles)

**Console Arc Desk R0.6 90, R0.7 120 and R0.8 180**: a desk top bent round a seat set at the part's origin, in 3, 4 or 6 level facets with a modesty panel under each and feet on the floor grid. The facets are looks (a shaped box), each with a hidden solid box inside whose top is the facet's: weld Console Bays onto them with the attach tool's socket weld (a face point), turned to the facet's angle. The bays stay flat; the desk carries the curve. Not done: bays that bend themselves, curved screens, sealed hull instruments.

Tried (phases 4 and 6): `--window-shot` seated at a Roamer couch set to Command (the COMMAND, STATIONS and HELM read-only sections); a drawer spawned on its own, F slid the tray out with its instruments on it, and F flipped the toggle on the open tray. Not tried: a crew manning a station seat, the drawer in a bay dug into a wall, a joined client, an arc desk with bays welded on.

## Command rooms by ship size (phase 5)

Karl, 2026-11-22: the storyboard's command rooms go into the ships themselves, each drawn in its own ship's generator in its faction's style. There is no shared room kit (ships must never look alike); the rooms use the pieces above (console bays, panel instruments, drawers, arc desks) and seats set to a station (phase 4).

- **By crew:** a pod for 1 to 2 (the pilot commands: the drive seat does when no seat is set to Command), a command room for 4 to 6 (stations facing a screen wall, the captain on a 0.3 m step behind them), a command deck for 8 to 14 (station pits, a 0.6 m dais with the plot table, two ways out). From the captain's seat every station's screen is in view.
- **By faction:** Militia a combat information centre (couches, swing-arm displays, guarded switches); Traders a merchant bridge (a long front console, a chart table, comms and cargo stations); Salvagers an operations bridge over the work bay.
- A seat's station goes in the blueprint as the step's `"setting"` (the `SeatStation` number). A room passes `--check-building` and, near a shaft, `--ladder-check`; `--crew-check SHIP` lists each crew member's station seat.

**The Zero Day** (`TorpedoFrigateParts.CommandCentre`, `CicSeats`): the bridge keeps the helm and the weapons officer forward; a second row takes the navigator (port) and sensors (starboard) under swing-arm displays; the captain's couch stands on a 0.3 m dais aft to starboard, the bridge's door moved to port to make room; ops behind takes damage control and comms at a raked console on its starboard wall. Next: the Packet's merchant bridge, then the Grapnel's operations bridge, then the pods.
