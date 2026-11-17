# Design files for work in progress

Written by design agents for the builders that follow; each ends with the user's answers, which override the text above them.

- `press-rework.md`: the Press rework (sized parts). Section 10 holds the phases, section 12 the user's answers. Phases 1 and 2 are on main.
- `smart-logistics.md`: the smart logistics network. Section 15 holds the user's answers. Phase 1 is on main. `smart-logistics-brief.md` is the user's decisions it was designed from.
- `linked-logistics.md`: logistics across constructs and worlds (Transmitter/Receiver pairs, Cargo Docks, cargo ship routes, the Dispatcher). Section 10 holds the open questions.
- `ship-works.md`: engines and turrets with insides (drive works behind a mount, pods, Turret Rings, autoloaders, reload hoists, capacitors and lasers). Section 12 holds the phases, section 14 Karl's answers to its seven questions. Nothing built yet.
- `bot-brain.md`: one brain for every AI in Sandbox (crew as bodies in seats and stations, the driving rule, LOD tiers, faction rules as data in `Crew/Factions`, boarding and scuttling, your own Robot Shell crew). Section 15 holds the phases, 16 the open questions, 17 what is to be agreed with Karl. Nothing built yet.
- `faction-controllers.md`: the bot brain's two top tiers: a faction controller per power (strategy, relations that change through play, pacts, missions, planets held) and admirals for groups (convoys, patrols, strike groups); far groups as one record. Section 10 holds the phases, 11 the open questions, 12 what is to be agreed with Karl. Nothing built yet.
- `factory-generator.md`: the factory generator (function first: brief, programme, flow graph, zones, grid plan with routed logistics, shell of halls and yards, faction style, wiring, checks that run it), modelled on the ship designer and the vehicle kit; the prerequisite for faction production. Section 9 holds the phases, 10 the open questions. Nothing built yet.
- `conveyor-inclines.md`: conveyors and chutes up and over aisles: grades by bed (roller, belt, cleated), vertical curves, the router's ramp pass, Piece Lift, Bucket Elevator, Spiral Chute, trestles and Link Bridges with clearances, the factory kit's overhead layer; legacy links unchanged. Section 11 holds the phases, 12 the open questions. Nothing built yet.

`tools/coordinator/merge.sh` is the coordinator's merge helper (merge, build, part checks, production sections by name, push); its paths are one cloud box's and need adjusting.

A design file is history once its last phase is on main: the code guide is `docs/sandbox/constructs/production.md`.

State on 2026-11-12, end of the coordinator session: Press phases 1-3 on main (3b left: Shaft, Actuator, Spring, Cable, Control, Tank, Beam dies; `press-phase3-notes.md`); smart logistics phases 1 and 2 in part (one-level chains; the remainder's plan is in `smart-logistics-phase2-notes.md`); the machine screens' Recipe button (`recipe-select-notes.md`: what is left and how the Press adopts it).
