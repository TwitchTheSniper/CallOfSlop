# Modular cockpits

Karl, 2026-11-22: buttons, screens, gauges, levers and lamps mount on consoles and on the hull with the same mounting system as the in-wall bays (module sockets with their styles), and a cockpit is linked to its seat, so a seat's state (dampeners off and so on) shows on the dash. The storyboard is the Artifact https://claude.ai/artifact/R99CRAFv1fxTaH9MGc6w1m and the plan the Claude Doc https://claude.ai/artifact/HxJ53EmqN1uUDpkPtdBNxS. Phases 1 to 3 are built (below); phases 4 to 6 (command stations and crew kinds, ShipGen's console slots and command rooms, curved bays and pop-up screens) are not.

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
