# Constructs and the Sandbox mode: milestones 1 and 2

The sandbox survival mode (board item dev-idea-constructs-cs01; the full design and the developer's 16 decisions are in
the project doc `claude/sandbox-construct-mode.md`) is being built in milestones. **Milestone 1 is the construct core,
proven on one small test map**: parts, tools in hand, ghost sockets, one rigid body per welded group, a working
drivetrain, a passive hinge, per-part damage with wrecks, and all of it server-authoritative over the network. There is no
large world, streaming, factions, enemies, points of interest, resources, scanning, terrain digging or construct weapons
yet. **Milestone 2 adds joints, logic and the drivetrain as parts** (decisions 3 and 4): motorised hinges, turntables,
pistons and steering pivots; logic parts wired with a linking tool; transmissions and differentials you can shoot;
passengers, riding on moving decks and run-over damage; and the yard's lift, turret, motorised gate and wired buggy to
test them on. Everything new is listed under [Milestone 2](#milestone-2-joints-logic-and-the-drivetrain). **Milestone 3**
(in parts) adds [salvage and the inventory](#salvage-and-the-inventory-milestone-3): taking what survives off wrecks, scrap
from what was destroyed, building from counted parts, and wrecks cleared away when left alone; and
[construct weapons](#construct-weapons-milestone-3): guns put together on a construct from the weapon builder's parts or
as heavy guns, fixed or on turrets, fired by a gunner or by logic (sentries), fed from ammo boxes that cook off, with the
weapon builder's penetrating, explosive and incendiary rounds.

## Playing it

Host a game on **Construct Yard** (Dev pack) and pick **Sandbox** (a Dev-only mode, like the template). The yard has a
buggy, a wired buggy (a passenger seat, a horn on the driver's fire key, headlamps on aim), an anchored gate, a gate on a
motorised hinge with a button, a lift, a turret on a turntable with a lever, a parts rack (the yellow shelf in the
north-west corner) and some loose parts.

| Key | What it does in Sandbox |
| --- | --- |
| 3 / 4 / 5 | Take out the **wrench** / the **welder** / the **linking tool** (press again to put it away). 1 or 2 go back to your guns. (4 and 5 are the kill-streak keys elsewhere; Sandbox has no streaks.) |
| F | At the rack: take a part (press again for the next kind). On a seat: sit (the first seat drives, any other rides). On a door, hinge, motorised hinge or piston: open or shut it (unless its wires run it, or it's locked). On a button, switch or lever: work it. On a loose part: pick it up. In a seat: get out. |
| Fire (hold) | With a tool out and a part in hand: fit the part on the ghost socket under the crosshair, or set it down on the surface you aim at as a **new construct** ([New constructs](#new-constructs-free-placement)). Letting go early stops it. |
| Arrow keys | Turn the ghost part 90 degrees as you see it ([Turning the ghost](#turning-the-ghost)): Left / Right about the vertical, Up / Down tip it towards / away from you, Shift + Left / Right roll it. The same on a socket and on a surface. |
| R | Put the ghost part back the default way round (on a socket: the first way that fits; on a surface: upright, facing your look). |
| F (welder out) | On a part that touches another construct: weld the two into one. |
| Aim (hold) | With a tool out: take the part under the crosshair off (into your hand, or dropped if your hands are full). |
| T | Drop the part in your hand. |
| Tab | The **inventory** (below): any part into your hands, any blueprint in front of you, a tool out. Tab again or Esc closes it. Sandbox has no scoreboard. |
| Middle mouse (hold) | The **tool wheel** (below): flick to a tool, let go to take it out; let go in the middle to cancel. |
| W S A D, Space | Driving: throttle, reverse (brakes first), steering, brake. |
| Fire / Aim / R (seated) | Held keys the construct's **input readers** can read (drivers and passengers don't shoot). |
| Physics gun (tool wheel, north-east) | Hold Fire to grab and carry a construct, wreck or loose part; wheel: distance; F + mouse: turn it (Shift snaps); Aim: freeze it; R twice: unfreeze yours ([The physics gun](#the-physics-gun)). |
| Linking tool | Routed links ([Routed links](#routed-links-decision-20)): hold the tool wheel key for the **link wheel** (data, power, pipe, shaft, chute; rest on Pipe for its styles, on Edit for delete / move node / re-route / highlight all, on Tools for the tool wheel). Fire on an output, Fire for each node, Fire on an input to finish; Aim or Backspace takes the last node off, Esc cancels. Aim on a part with nothing routed: cut its data links. R on a logic part: its next setting (a preset). |
| Configure tool | (The tool wheel, SE.) Fire on a part: its settings panel. Shift+Fire: select more parts of the same kind (changed together). Aim: clear the selection. [The configure tool](#the-configure-tool). |

### Turning the ghost

Board item dev-sandbox-arrow-rotate-ar02 (`Source/Constructs/GhostTurn.cs`). Every ghost (free placement on a surface
and attaching to a socket) turns with the same keys, each a quarter turn relative to the camera:

- **Left / Right** (the arrow keys): about the vertical; Left swings the part's front to your left, whichever way you or
  the construct face.
- **Up / Down**: about your view's right axis; Up tips the part's top towards you (its front rises), Down away.
- **Shift + Left / Right**: roll about the axis pointing away from you; roll left takes the top to your left.
- **R**: back to the default orientation (it no longer steps through the turns).

The camera's up and right are snapped to the nearest axes of the frame the part's turn lives in (the new construct's frame,
which follows the look's yaw and the surface; or the construct the socket is on), so every result is one of the 24 `Rot24`
turns the placement and the network already use: no wire change. On a socket the part's own sockets decide which turns
are possible: a turn no socket of the part can make there (or one that would be in the way) is skipped and the next quarter
turn the same way is taken, and of the ways round giving one turn the first is used, so a key and its opposite always undo
each other. The ghost carries a small gizmo: its front as an orange arrow, its top green, its right red. The HUD's hint
names the keys ("<- -> ^ v: turn, Shift + <- ->: roll, R: reset").

The keys are rebindable in Settings > Controls under the **Sandbox** heading: Turn the part left / right, Tip it towards /
away from you, Roll it left / right (binds version 12). Sandbox actions are live only in Sandbox and the Zombies spectate
keys only outside it, so they share the arrows without a clash (`Keybinds.Context`); a Sandbox action may be bound to a
Shift chord (hold Shift while choosing the key), and while a chord is held its plain key does nothing (Shift + Left rolls,
it doesn't also turn).

**Self-test**: the **Sandbox ghost turns** section (`Source/App/SelfTestGhostTurn.cs`): from every player yaw (15 degree
steps), on level, wall and upside-down frames, from all 24 turns: a key then its opposite gives the same turn, four presses
a full turn, each an exact quarter turn about the camera's snapped axis the right way (on a slope only the turn rules); on
a Frame Block's every socket, at three construct yaws and every 30 degrees of look, a key gives the same turn as free
placement in the same frame, its opposite comes back to the default, four come round; a Frame Beam (which can't take every
turn there) still undoes; the default keys, contexts and chords; a binds version 11 file gets the keys, a taken key is
kept; nothing allocates.

### The tool wheel (decision 17)

The developer's decision 17 (2026-10-29): players pick their tools from a radial menu. Hold **Tool wheel**
(`GameAction.ToolWheel`, the **middle mouse button** by default, rebindable; added by binds version 10. Q was taken by
Swap weapon, and the middle button was free everywhere) and eight segments appear around the middle of the screen,
clockwise from the top: **wrench** (N), **physics gun** (NE; it took the hoist's place, [below](#the-physics-gun)), **welder** (E), configure tool (SE), **hands / gun** (S: out of
build mode), scanner (SW), **linking tool** (W), hand drill (NW). The four tools that exist sit on the compass points, so
the easiest flicks reach them, with the physics gun on the north-east; the configure tool on the south-east
([The configure tool](#the-configure-tool); no number key: the wheel or the inventory's Tools tab); the two from the
design's tool list that don't exist yet sit on the other diagonals, greyed out with "coming later" (letting go on one says so and changes nothing). While the key is held the view stands still and the
mouse moves the wheel's pointer instead (accumulated, held within 70 px of the middle, so a small flick turns it to
another segment); the segment is picked by direction (a 22 px dead zone in the middle). The highlighted segment gets an
orange arc and its name and favourite key show in the middle ("key 5: wires, settings"); the tool in your hands is marked
with a green inner arc and dot. Letting go takes the tool out through the very request the number keys send
(`CActKind.Tool`, server-authoritative), so nothing on the wire changed (protocol 79) and the number keys stay as the
favourites. Letting go in the middle cancels. You keep walking while it's open; the mouse buttons don't reach the gun or
the tools meanwhile (so opening it lets go of a fit in progress). It doesn't open in a seat ("get out of the seat
first"), and it closes without choosing on death, any menu (the parts menu included: only one is open at a time), the
chat, or the match ending; a key still held after that doesn't reopen it.

The widget is generic (`Source/UI/RadialMenu.cs`): items with a label, a hint, a note, an enabled flag and an icon id; the
owner opens it, feeds it mouse movement and asks which segment was chosen on release; it draws with ImGui's draw list
(annular sectors, arcs, the icons through a delegate) and allocates nothing per frame. It is meant to be reused for parts
to place, logic components, pings or emotes. The Sandbox wheel is `Source/UI/SandboxToolWheel.cs` (its icons are simple
shapes from lines and boxes: an open-jaw wrench, a torch with sparks, two terminals and a wire, a box pistol, a hook on a
chain, three sliders, a handset with waves, a drill); `Source/App/Program.SandboxWheel.cs` hooks it into the frame.

Choices made for the wheel (the developer may change them): the middle mouse button, not Q (Q swaps weapons); the wheel
sits at the screen centre (the cursor is captured, so "around the cursor" is the centre); the tools still to come are
shown greyed rather than left off (eight segments read cleanly as a compass, and they show what is planned); it is
closed in a seat (seated keys belong to the construct's input readers); no tool modes ring or second page yet (no tool
has modes); no stick support yet (no Steam Input in the game).

### Tools in hand

Every tool has its own **held model** (board item dev-sandbox-tool-models-tm01), built from boxes as data in
`Constructs/Tools/*.json`, drawn in place of the gun while the tool is out: in first person with the view model's arms
(both hands on their grips through the same IK as the guns) and on other players and bots in third person (the same
place a gun sits, with `ThirdPersonArms` holding it). `Source/Render/ToolRender.cs` poses and draws them;
`Source/Constructs/ConstructTools.cs` loads the files.

- **Wrench**: a heavy adjustable spanner held upright; it **cranks** back and forth while a fit or a removal runs.
- **Welder**: a gas torch with a blue bottle, the off hand under the barrel; while welding it shakes, its tip glows and
  flickers, it throws **sparks**, and on a lit map it lights its surroundings with a flickering blue-white arc
  (`LightGrid.AddDynamic`, one per welding player; unlit maps have the glow and sparks only).
- **Linking tool**: a wiring probe with a glowing screen and a reel of wire; while a wire is being run (yours: from the
  first click to the second) the reel turns and the probe throws a green **beam**.
- **Configure tool**: a handset with a screen, sliders and a dial; it is "in use" (its motion) while a settings panel is open.
- **Physics gun**: an amber beam projector held in both hands; while it holds something its coil spins, it shakes a
  little and its tip lights the map, and a curved beam runs to the held thing ([below](#the-physics-gun)).
- Still to come, with models ready for when the tools exist (the tool wheel's diagonals): the **hoist** (a winch with a
  hook on a chain), the **scanner** (a turning emitter and an aerial; a beam while it scans) and the **hand drill** (its
  bit spins while it works).

Every tool has an idle sway (and a walk bob in first person). **Equipping**: taking out a tool (the number keys or the
tool wheel) drops the one in your hands out of sight (0.14 s) and brings the new one up (0.24 s); from the gun the tool
just comes up; putting a tool away drops it, then the gun comes up with its own equip (it can't fire until then). A
**part being carried** to place (from the rack, the parts menu or picked up) shows in the off hand, scaled down to fit
(the ghost on the construct shows where it goes, as before); with no tool out it is carried in both hands. In a seat
nothing is in the hands.

Remote players render it from what the Sandbox mode-state tail already sends (every player's tool, held part and edit in
progress), so nothing on the wire changed (protocol 79). The linking tool's beam shows only in your own hands (the wire
in progress isn't sent). Models are built once at load; drawing allocates nothing (fixed per-player state; a struct box
sink feeds either the renderer or the scene graph's draw list, so the scene graph and the immediate code draw the same).

A tool file:

```json
{
  "format": "call-of-slop-construct-tool/1",
  "name": "Welder",
  "tool": "welder",
  "description": "A gas welding torch...",
  "hold": [4, 10, 0],
  "boxes": [
    { "size": [0.032, 0.1, 0.04], "at": [0, -0.02, 0.005], "color": "#2b2b2e" },
    { "size": [0.018, 0.018, 0.016], "at": [0, 0.06, -0.31], "color": "#9fd8ff", "glow": true }
  ],
  "grip": { "at": [0, 0, 0], "rot": [-14, 0, 0], "shape": "wrap" },
  "support": { "at": [0, 0.02, -0.13], "shape": "under" },
  "tip": [0, 0.06, -0.32],
  "use": { "motion": "jitter", "amount": 0.006, "rate": 23, "sparks": "#ffb040", "light": "#a8d4ff" }
}
```

| Field | Meaning |
| --- | --- |
| space | Like a gun's: metres, the origin where the strong hand holds it, -Z forward, +Y up. Third person draws it 1.3 times as big, as guns are. |
| `tool` | `wrench`, `welder`, `linker`, `physgun`, `configure`; or one still to come: `hoist`, `scanner`, `drill`. One file a tool. |
| `hold` | How it is turned in the hand (degrees: pitch, yaw, roll), about the grip. |
| `boxes` | Up to 24: `size` (and `at`) or `min`/`max`, each 2 mm to 60 cm a side, `color`; `"glow": true` draws it unlit and bright (flickering in use, a slow pulse otherwise); `"spin": true` turns it about the `spin` section's `axis` through its `pivot`. The boxes must touch into one piece. |
| `grip` / `support` | The strong hand (`at` within 15 cm of the origin) and the off hand (optional: without it the off hand rests): `at`, `rot` (degrees), `shape` (`wrap`, `under`, `cup`, `flat`). |
| `tip` | Where sparks, the light and the beam come from. |
| `use` | While it works: `motion` (`ratchet`: turns `amount` radians about `axis`; `jitter`: shakes `amount` metres; `push`: jabs forward `amount` metres; `sweep`: turns side to side; `none`) at `rate` Hz; `spin` / `idleSpin` (radians a second for the spin boxes); `sparks`, `light`, `beam` (colours) and `beamLength` (metres). |

A bad file is refused with the reason (`--check-construct-parts` lists the tools too) and that tool is drawn as a plain
grey block. `--tool-shots` writes pictures of every tool in first and third person, idle and in use, a carried part
(with the wrench and in bare hands) and a swap half-way (`smoke_tool_*.bmp`); it runs under xvfb like `--light-shots`.

Choices made (the developer may change them): the wrench is one-handed and upright (the off hand rests), the welder and
scanner two-handed; a carried part goes in the off hand, scaled down (a frame block would hide the view at full size);
putting a tool away brings the gun up with its equip time before it can fire; the welder's light shows only on lit maps
(the dynamic lights live in the light grid); the tools still to come have models but stay greyed on the wheel.

### The physics gun

Board item dev-sandbox-physics-gun-pg01 and decision 19. A Sandbox tool (`ConstructTool.PhysicsGun`, its wire byte 8,
after the tools still to come) on the tool wheel's north-east segment and in the inventory's Tools tab, with its own held
model (`Constructs/Tools/PhysicsGun.json`: an amber projector with a spinning coil and three prongs, two-handed; its
coil spins and its tip lights the map while it holds something). The keys are the existing actions, so they rebind:

| Key | With the physics gun out |
| --- | --- |
| Fire (hold) | Grab the construct, wreck or loose part under the crosshair (30 m reach; 12 m in survival). Let go to drop it (it keeps its speed: a flick throws it). |
| Mouse wheel | While holding: pull it in or push it out (12% of the distance a notch, at least 25 cm; 1.2 m nearest). |
| Interact (F) held + mouse | While holding: turn it (about up, and about your right); the view stands still meanwhile. Sprint (Shift) snaps the turn to whole 45 degree steps of yaw, pitch and roll. |
| Aim | While holding: **freeze** it where it is and let go. |
| Reload twice within 0.35 s | Unfreeze everything you froze (the host holding Sprint: everything, anyone's). The HUD says how many. |

- **Holding**: the grab point keeps its offset on the thing, and the thing keeps its turn relative to your look's yaw
  (turn round and it swings round with you). Each tick the server sets the beam's point (your eye plus your look times
  the distance) and the wanted turn; inside the physics step, after gravity, a spring (`ConstructGrab.Drive`) pushes the
  body's velocity towards the one that closes the gap over 0.1 s (at most 18 m/s) and its spin towards the one that
  closes the turn over 0.12 s (at most 9 rad/s), by no more than the beam's strength allows that tick plus gravity (so it
  hangs in the air). The contacts are solved after it, so the held thing collides with the map and pushes other
  constructs instead of passing through them; it never teleports. A held body never sleeps.
- **Strength**: 200 kN in creative (at most 40 m/s2), things up to 15 t; survival 8 kN, up to 500 kg (heavier: "too
  heavy for the physics gun"), so heavy things are slow to pull.
- **Refused**: players (a construct with anyone in a seat), anything anchored to the ground (a foundation, a pinned
  construct), something another player holds (one holder per construct), and in survival an enemy construct that is
  contested (below). The look text says why before you click; a refused click flashes a short red beam and says why.
- **Frozen**: kinematic, held still exactly like an anchored construct (other things rest on it and bump into it), drawn
  with an icy tint and outline, and a small snowflake beside the crosshair while you look at it. Grabbing a frozen thing
  unfreezes it. Each construct remembers who froze it (`Construct.FrozenBy`); a player who leaves leaves their frozen
  things nobody's (only the host's "everything" reaches them).
- **Multiplayer**: server-authoritative. `CActKind.Grab` (the construct and the grab point, 1 cm), `GrabRelease`,
  `GrabFreeze` and `UnfreezeAll` go reliably; `GrabSteer` (distance and turn) goes unreliably on a change, at most ~30
  times a second and every 0.25 s anyway, like a seat's look. The held thing moves by the ordinary `CPose` poses (it is
  awake), so everyone sees it move smoothly; the mode-state tail (version 3) carries each player's held construct and
  grab point, so every machine draws everyone's beam: a curve from the tool's tip that leaves along the holder's look and
  bends onto the grab point. `CBody` carries the frozen flag and who froze it (protocol 88).
- **Saving**: world saving isn't built yet; the frozen state (flag and owner) is part of a construct's structure, which
  is what a save will hold, and the self-test round-trips it.
- **Cost**: nothing when unused: the physics checks one byte a body, the server one short a player.

**Contested constructs (decision 19)**, survival only (a map with infinite parts off; creative is unrestricted): every
editing tool (the physics gun, the wrench and welder fitting and taking off, the welder's join, the linking tool's wires
and settings; a tool still to come calls the same helper, `ConstructGrab.Defended` / `ConstructEdits.Contested`) is
refused on someone else's construct while its owner (a player or a bot; factions don't exist yet, so the owner is the
faction) is alive within `ConstructGrab.ContestRadius` (40 m, tunable) of it plus its own size, which covers being in it:
"Owned by Name - defended". The map's constructs (nobody's), wrecks and your own are never contested; once the owner is
dead or far away it can be grabbed and salvaged. Shooting it apart works at any time.

Choices made (the developer may change them): the physics gun took the hoist's segment on the wheel (decision 19 has the
physics gun move and lift pieces; the hoist stays in the inventory's Tools tab as still to come); no new key binds (Fire,
Aim, Interact, Sprint, Reload and the wheel, all rebindable already); the held thing turns with your yaw (not fixed in
the world); "unfreeze everything" is the host's double-tap with Sprint held rather than a menu option; survival's caps
(8 kN, 500 kg, 12 m) and creative's (200 kN, 15 t, 30 m); the contested radius is from the owner to the construct's
centre of mass plus its radius.

### The inventory (the parts menu)

**Per-mode keys** (board item dev-sandbox-tab-inventory-ti01, binds version 13): Sandbox has no scoreboard, so the
inventory took Tab there without changing the global Scoreboard default. Each action has a context (`Keybinds.Context`):
everywhere, Sandbox only, or every mode but Sandbox. The Scoreboard and the Zombies parts inventory (still on I) are live
outside Sandbox only, the Sandbox inventory and the other Sandbox keys in Sandbox only, and two actions clash on a key only
when their contexts overlap, so the keybinds screen (the Sandbox keys under their own heading) treats Tab there as no
conflict. Settings migrate once: a player who never rebound the inventory gets Tab; one who had rebound it keeps that key
for the Sandbox inventory; if a key used everywhere already holds Tab, the Sandbox inventory keeps I. The
**Sandbox Tab inventory** self-test section (`Source/App/SelfTestSandboxTab.cs`) holds Tab in Sandbox, Team Deathmatch and
Zombies matches and migrates version 12 files.

**Tab** (the Sandbox inventory key, `GameAction.SandboxInventory`; rebindable under the Sandbox heading in Settings >
Controls) opens the **Inventory** window (`Source/UI/UiSandboxParts.cs`), the mouse free for it (it replaced milestone 1's "Parts (debug)"
list, 2026-10-29; the developer asked for "a nice inventory UI with 3D tiles and categories"):

- **Tabs**: Parts, Blueprints and Tools.
- **Left**: a search box (name, group, section, role or description) and a **category tree**: "All", then the groups
  (Structure, Beams, Panels, Armour, Shapes, Wheels and drivetrain, Seats, Joints, Logic, then Power, Weapons, Storage or
  Other when parts of those kinds exist) with their sections (square beams, I-beams, braces...; sheet steel, glass,
  openings...; logic by kind: controls, sensors, gates, timing, outputs). A part file may name its own group and section
  (`"group"`, `"section"`: the structural parts do); otherwise it goes by its role.
  The tree is made from the loaded data (`Source/UI/SandboxCatalogue.cs`), never from a list of part names: each part
  goes by its role, and a **role this build doesn't know** (a new one from another branch) goes by a word in its name
  (power, battery, gun, turret, storage...) or else into **Other** under its own name. Blueprints are grouped as vehicles
  (a seat and wheels), machines (joints) or structures; tools as in the game or still to come. Below the tree: "Keep open
  after picking" and the tile size slider.
- **Right**: a scrolling grid of square **3D tiles** under big headers (a group's tiles flow together; a section picked
  in the tree gets its own header), each the thing drawn from its boxes at a 3/4 view with the default light. A tile's
  dot is the tool it needs (orange welder, blue wrench, green linking tool), its badge how many you have, and hovering
  shows its name, tool, mass, health, armour, fitting time, how many you have and its description. Above the grid: what
  is in your hands, how many tiles are shown, and the **recent** row (small tiles of your last eight parts).
- **Picking**: click a tile, or move the highlight with the arrow keys and press Enter. A part: the client asks the
  server for it by id (the rack's `CActKind.Rack` request with a part id, so the server checks it: an unknown id is
  refused) and takes out the tool it needs, so the ghost sockets, fitting and dropping work exactly as with a part from
  the rack. A blueprint: the server builds it standing on the ground in front of you, facing the way you look, far
  enough out not to land on you (`CActKind.Blueprint`; refused outside Sandbox). A tool (one in the game): it comes out
  (again: put away). The window closes after a pick unless "Keep open after picking" is ticked; Tab again or Esc closes it.
  It closes itself when you die, sit down or the match ends, and it isn't offered in a seat. The rack works as before.
- **Counts**: the badges read an `ISandboxPartCounts` source (`Ui.SandboxCounts`; the salvage work's
  `Game.SandboxInventory` is meant to be wired in there). With no source, or a negative count, a badge shows **inf**
  (infinite parts, as now); a count of 0 shows red (the tile can still be clicked: the server decides).

**The tile atlas** (`Source/Render/SandboxTiles.cs`): every part, blueprint (built in a scratch construct world through
the server's own rules: joints at rest, wheels on their springs) and tool model is drawn once into 128 px tiles of
2048 px atlas pages (256 tiles a page; slots are parts by id, then blueprints, then tools). Each tile is drawn at twice
its size into a small offscreen framebuffer, orthographic and framed tight round its boxes, on a clear backdrop, and
blitted down into its page (smooth edges), then the page's mipmaps are made: plain framebuffer objects and blits, the
same on the GL 4.1 and 4.6 paths. The window asks for the atlas when it opens; the main loop's `SandboxTiles.Pump`, before
the scene, draws up to 12 tiles a frame within a 3 ms budget (the first frame's GL set-up aside), so opening the window
never stalls a frame for long (on Mesa's software GL the 52 tiles took about 20 frames). The atlas is kept for the session
and made again only when the loaded parts, blueprints or tools change; until a tile is ready (or if the framebuffer can't
be made) its card shows a plain colour swatch instead.

It is only in Sandbox, and a map can turn it off with `"partsMenu": false` in its `"sandbox"` section (a future
survival map will): the key then says "the parts menu is off on this map" and the server refuses both requests. The
window allocates nothing per frame while open (every tile's text is made once; the catalogue's key is taken once).

Editing always needs the right tool in hand (decision 2): **welder** for frames, panels, corners, decks and foundations,
**wrench** for everything bolted (seats, wheels, suspensions, engines, tanks, fuel lines, transmissions, differentials,
hinges and joints, doors, logic parts), the **linking tool** for wires and logic settings. With a gun out
or bare hands you can only use things: sit, drive, open doors, pick up and drop loose parts. Fitting takes the part's
`attachSeconds` (welded parts 2.5 s, bolted 1 to 3 s), taking off 60% of that. The HUD panel shows what is in your hands,
what you look at, the edit's progress, refusals and, when driving, speed, fuel and why it won't go.

## What a construct is

- A **construct** is a set of parts joined by **links** (a weld or a bolt between two sockets). Everything joined
  directly is **one rigid body**; parts that hang on a **hinge** are in a **segment** that turns about the hinge (a
  joint is an angle on the body, so it never drifts apart).
- The **root** is the first seat, else the first foundation, else the first part. After any part goes, everything no
  longer joined to the root splits off as a **wreck** (its own body, moving as that piece moved). The broken part itself
  falls away as wrecked debris. A wreck has no root, can be edited like anything else, and can't be picked up whole.
- **Anchored**: a working foundation holds the construct still (a base, a gate), or it was set down **pinned** (creative's
  "anchor to the ground"). Free constructs move by physics.
- **Mass and centre of mass** come from the parts; inertia from their boxes.
- Parts sit on a 1 cm grid and turn in 90 degree steps (24 turns), so every part box is axis-aligned in its body and
  overlap checks are plain box tests.
- **Ghost sockets**: holding a part with a tool out shows a marker on every free socket of the construct you look at
  that the part fits (matching socket kinds, facing each other, nothing in the way); the one nearest the crosshair shows
  the whole part, green when it fits.

### The drivetrain (it only works when connected)

(Since the drivetrain families the drive line can also run through shafts, gearboxes, chains, clutches and transfer cases
to differentials and hubs: [Drivetrain parts](#drivetrain-parts-the-families), below.) A construct drives when it has a
working **seat** with a driver (or a signal wired into its engine), a working **engine**
in the body, **fuel** reaching the engine (a working tank linked to the engine directly, or through a chain of working
**fuel lines**), a working **transmission** touching the engine, and **wheels on working suspensions** bolted to a working
**differential** the transmission feeds (touching it, or through other differentials), directly or through a steering
pivot. Only those wheels are driven; any other wheel rolls (and steers, and brakes). A missing or broken link stops it, and
the HUD says which: "no fuel line from the tank to the engine", "the transmission is wrecked", "no wheels on the
differential"... Transmissions and differentials stop working at zero health (`disable`) rather than falling off.

### Physics

`Source/Constructs/ConstructPhysics.cs`, server only, at the fixed 60 Hz tick:

- Rigid bodies with box inertia, integrated with gravity (the game's own gravity and the host's gravity setting).
- Against the map: up to 40 **hull points** per body (part corners, the outermost kept), each probed with a ray down and
  a ray along its motion, giving speculative contacts solved with sequential impulses and friction.
- Between constructs: hull points of one against the part boxes of the other.
- **The parts beyond joints collide too** ([Joint collision](#joint-collision), below): against the map, other
  constructs and wrecks, with the joint solved together with those contacts.
- **Raycast wheels**: a spring-damper along each suspension (stiffness from the body's mass and the suspension's
  frequency), side grip, drive force shared by the touching wheels, brakes, a friction circle, and tyre forces applied
  most of the way up to the centre of mass (an arcade anti-roll bar) so a buggy turns without rolling over.
- **Joints** (reduced coordinates: each is one number on its body): a passive hinge is damped, falls under gravity,
  is held inside its limits, latched open or shut by Interact, swung by a player walking into its leaf, and held still
  while its lock is on; the moving joints are driven by a motor (milestone 2, below).
- A body that stays still for 40 ticks sleeps and costs nothing until a hit, an edit, a driver or a nearby body wakes it.
  An anchored construct with joints sleeps too once its joints are still (a motor that is still pushing, stalled
  against something, keeps it awake); a motor asked to move, more riders than it can hold, Interact or a push wake it.
- Players are never moved by physics here: they are pushed out of construct parts (or stand on top of them, carried
  along by the body and by any joint under their feet), and drivers and passengers are pinned to their seats (and drawn
  as part of the construct: "Seats" below). A moving
  part that moves into a player shoves them along and, faster than 4 m/s of its own speed, hurts them (run-over damage,
  below).

### Damage (decision 6)

Every part has its own **health** and **armour** (a flat amount taken off every hit). Bullets hit the part they reach
(construct parts stop bullets and shield what is behind them); blasts (grenades, launchers, kill streaks, explosive props)
hurt every part in range, falling off with distance, and push the bodies. At zero health a part either **breaks off**
(`"onBreak": "detach"`, the default) or **stops working** (`"disable"`: the default for engines, tanks and seats; a wrecked
tank loses its fuel). Shooting a cheap fuel line or a suspension disables a vehicle without wrecking it: the loot loop's
core (salvage itself is milestone 2).

## The configure tool

Board item dev-sandbox-configure-tool-ct01. The configure tool (the tool wheel's SE segment, or the inventory's Tools
tab) changes a part's **settings**. With it out, the look line names the part and how many settings it has; **Fire**
on a part opens its **settings panel** (the mouse comes free for it); **Shift+Fire** adds a part to (or takes it out
of) a selection of parts of the same kind on the same construct, which the panel then changes together; **Aim** clears
the selection. The panel closes on Esc, its Close button, a click away from it, putting the tool away, sitting, dying,
the part going, or walking more than 10 m from it.

### The panel

`Source/UI/UiSandboxConfig.cs`, drawn like the inventory (the same window style; readable at any UI scale):

- **Anchored beside the part**: the card sits to the right of the part on screen (to its left near the edge), and
  follows it as you move; with the part off screen it sits at the right edge.
- **Header**: the part's tile (the inventory's picture of it), its name, the construct (number, and whose: yours,
  nobody's or the owner's name), the part number and a health bar.
- **Settings, grouped** as the part file groups them (Timing, Light, Motor...), each with a plain label (units in the
  value: "2.5 s", "45 deg", "60 %"), a tooltip (what it does, its range and default) and a small `x` that puts that one
  setting back to its default. The controls by type: **number**: a slider (Ctrl+click it to type) and a numeric box
  (Enter sets it), held inside its range and on its step; **choice**: a dropdown; **toggle**: a check box; **colour**: a
  colour picker and eight swatches; **key** (an input reader's key): "Press to bind" then the key itself (only keys a
  seat passes on: fire, aim, reload, jump, sprint, forward, back, left, right; Esc cancels), or the dropdown.
- **Wiring**: every wire into and out of the part ("In from: Switch (part 3)"); hovering a row highlights the part at
  the other end in the world, clicking it opens that part's settings.
- **Reset** (every setting to its default), **Copy** and **Paste** (only onto a part of the same kind; the button names
  the kind it holds), **Change all N selected** (on by default with a selection) and **Apply to selected** (every value
  shown onto all of them).
- Changes go to the server as they are made: a dropdown, toggle, swatch or typed number at once, a dragged slider at
  most ten times a second and when it is let go. The server's values flow back into the panel (a value just sent is
  kept until the server's answer arrives or 0.6 s pass). Nothing is allocated per frame while it is open (every text is
  made once; the health and wiring lines again only when they change).

**Live previews** in the world while the panel is open (`Source/Render/ConstructRenderConfig.cs`, seen through parts,
following the panel's values before the server answers): the part tinted purple (and the selected ones fainter), its
wires (green in, orange out) and the parts at their ends; a **lamp** lit in the colour and brightness being picked; a
**piston's** travel from its pulled-in to its pushed-out end with a ghost of the leaf at full travel; a **hinge's**
limits as an arc about its pivot, with a line where it is now; a **proximity sensor's** range as three rings.

### Settings are data

Each part file lists its settings in `"config"` (up to 8), so a new part gets its panel by itself:

```json
"config": [
  { "key": "period", "label": "Period", "group": "Timing", "unit": "s", "min": 0.05, "max": 60, "step": 0.05, "tooltip": "How long one on-and-off cycle takes." },
  { "key": "startOn", "label": "Start on", "group": "Timing", "default": false, "tooltip": "..." },
  { "key": "colour", "type": "colour", "default": "#ffed99" },
  { "key": "mode", "options": ["both ways", "switching on", "switching off"], "default": 0 }
]
```

| Field | Meaning |
| --- | --- |
| `key` | What the setting means to the game (below); a key the game doesn't read, or one the part's kind doesn't use, refuses the file with the reason. |
| `type` | `number`, `choice`, `toggle`, `colour` or `key`; by default from the key (`startOn`, `latched`: toggle; `colour`; `mode`, `operator`, `units`, `setting`: choice; else number). |
| `min`, `max`, `step` | A number's range and step (0: any value). |
| `default` | A number, true/false, a colour, or a choice's option (name or index). Without it: what the part did before (a joint's limits, speed and strength; a suspension's spring), else 0. |
| `unit`, `label`, `group`, `tooltip` | What the panel shows (ASCII; the group defaults to "General"). |
| `options` | A choice's 2 to 16 names. |

**Folded in.** A part's own choice from before (`ConstructPartDef.Settings`: an input reader's key, a manual gearbox's
starting gear, a gear selector's positions, a lockable differential's lock, a transfer case's 2WD/4WD, an adjustable
suspension's presets) is a `setting` entry by itself (a dropdown; the reader's a key field), kept in the part's setting
byte as before, so the driver's shift keys, the HUD and the wiring work unchanged. A timer's, delay's, compare's,
scale's or proximity sensor's old `settings` list are now **presets** of its number (period, seconds, threshold,
factor, range): the linking tool's R steps through them as before, and the panel sets any value in range.

| Key | Parts | Does |
| --- | --- | --- |
| `period`, `duty`, `startOn` | Timer | Seconds a cycle; % of it on; each run starts with the on part |
| `seconds`, `mode` | Delay | How long; what waits (both ways, switching on only, switching off only; the other follows at once) |
| `operator`, `threshold`, `hysteresis` | Compare | > >= = <= < against the threshold; once on, it holds until the sum is that far back |
| `factor`, `offset`, `low`, `high` | Scale | sum x factor + offset, held between low and high |
| `latched`, `hold` | Button | Push on, push off; or on for `hold` seconds after a press |
| `startOn` | Switch, Toggle | A new one starts on |
| `radius` | Proximity Sensor | Its range (a sentry turret aims within it too) |
| `units`, `threshold` | Speed Sensor | m/s, km/h or mph; above 0, on/off at that speed instead of the number |
| `colour`, `brightness`, `blink` | Lamp | Its glow's colour and brightness; flashes a second while lit |
| `pitch`, `volume` | Horn | Its beep |
| `min`, `max`, `speed`, `strength` | Piston, Motor Hinge, Hinge (limits), Turntable (speed, strength) | Limits (degrees; a piston's metres) inside the part's own, speed, force or torque |
| `strength` | Brake | 100%: the wheels lock; less: they drag (`jFwd` blended towards a stop) |
| `stiffness`, `damping` | Suspension (a plain spring) | Spring frequency (Hz) and damping ratio |
| `setting` | Input Reader, gearboxes, selectors, differentials, transfer cases, adjustable suspensions | Their own choice, above |

Values live on the part (`CPart.Cfg`, eight floats inline, so they move with the part when parts are renumbered or a
construct splits; a colour is 0xRRGGBB, exact in a float), are read through `ConstructConfig.Get` (a table lookup, no
allocation), start at the defaults when a part is fitted, and are clamped wherever they are set. A part taken off,
stored or picked up loses its settings (it goes back to the inventory as a kind).

### Server, permissions and the network

The client asks with `CActKind.Configure` (protocol 89; [Network](#network-protocol-82-84-for-free-placement-86-for-the-drivetrains-rpm)); the
server (`ConstructEdits.Configure`, `Game.SandboxConfigRefusal`) checks the configure tool is out, the player isn't
seated, every part is of one kind on that construct (at most 32), the nearest is within 8 m, decision 19's shared
contested rule (`ConstructEdits.Contested`, the physics gun's: in survival an enemy construct with its owner alive near is
"defended"), and **who may**
(decision 19, factions treated as the owner for now): your own constructs, nobody's (the map's), wrecks and loose parts;
someone else's is refused, "Owned by NAME - defended" while its owner is alive within 40 m (plus the construct's size),
"Owned by NAME: only its owner can configure it" otherwise. It clamps every value, rebuilds a joint's limits, speed and
strength, and sends the construct's structure again (the settings ride in `CBody`). A refusal comes back as the usual
notice.

### Self-test

"Construct configure" (`SelfTestConstructConfig.cs`): every configurable part's schema is valid (slots, ranges,
defaults, options, presets on their step; a new part at its defaults, a joint's default speed its own) and the parts that
should have settings do; bad schemas are refused with a reason; values clamp (range, step, NaN, toggles, colours,
choices); each setting changes behaviour (a timer's period doubles its pulses and its duty sets its on time, a delay's
mode, a compare's operator, a scale's offset and limit, a brake's strength, a latched button and a hold time, a speed
sensor's units and threshold, a proximity sensor's range, a piston's travel, a motor hinge's limit and speed); the
server's checks (the tool, the reach, one kind, a missing slot, someone else's defended or not, your own and nobody's
allowed, several parts at once); a blueprint's `"config"` builds, is written back (`ConstructConfig.ToJson`) and builds the
same, and the construct message carries every setting; the panel headless in a game (values sent as the host, the
preview, copy and paste between lamps, refused onto a horn, reset, a selection changed together, no allocation over 60
open frames, refused with the tool put away); and over the real transport a client's change reaches the server and its
own copy, and the host's construct with the host beside it is refused as defended.

### Choices made (the developer may change them)

- Fire (not Use) opens the panel: Use keeps working buttons, switches and doors with the configure tool out.
- Changes apply live (no Apply button for a single part): the previews and the construct follow at once.
- Permissions follow decision 19: someone else's construct is refused whether or not it is defended (owner only for
  now); only the wording differs. Wrecks, loose parts and nobody's constructs are open to all.
- The configure tool has no number key (3, 4 and 5 are taken; the wheel and the inventory have it).
- Settings aren't kept by a part taken off or stored, and constructs aren't saved with the world yet; blueprints carry
  them.
- The panel's key field takes only keys a seat passes on to input readers (they are the only keys the server sees).
- Not done: a pressure plate's "who triggers" and weight, a proximity sensor's targets and line of sight, a lamp's light
  range (lamps cast no light yet), a seat's exposed keys, a lever's positions (the game has only players to detect, no
  lamp lights, and seats pass on a fixed set of keys). Controller support waits for Steam Input.

## The data

### Part files: `Constructs/Parts/*.json`

One part per file, loaded at start-up; ids follow the sorted file names. A bad file is refused with a reason (the rest
still load): `CallOfSlop --check-construct-parts` prints every part and blueprint, or the reason it was refused.

```json
{
  "format": "call-of-slop-construct-part/1",
  "name": "Suspension",
  "description": "A sprung wheel mount: bolt it to a frame's side, then a wheel onto it.",
  "role": "suspension",
  "tool": "wrench",
  "attachSeconds": 1.5,
  "mass": 15, "health": 110, "armour": 3, "material": "steel",
  "color": "#b08a2e",
  "boxes": [
    { "size": [0.3, 0.3, 0.3] },
    { "size": [0.12, 0.44, 0.12], "color": "#d0b050", "detail": true }
  ],
  "sockets": [
    { "name": "mount", "at": [-0.15, 0, 0], "dir": "-x" },
    { "name": "axle", "at": [0.15, 0, 0], "dir": "+x", "kind": "axle" }
  ],
  "suspension": { "travel": 0.3, "frequency": 2.2, "damping": 0.7 }
}
```

| Field | Meaning |
| --- | --- |
| `name` | Unique, 1-32 ASCII characters. Blueprints, maps and the rack name parts by it. |
| `role` | `frame`, `panel`, `seat`, `wheel`, `suspension`, `engine`, `fueltank`, `fuelline`, `hinge`, `door`, `foundation`; milestone 2: `joint`, `transmission`, `differential`, `logic`; milestone 3: `gun`, `gunpart`, `ammobox`, `feedchute` ([Construct weapons](#construct-weapons-milestone-3)). |
| `tool` | `wrench` (bolted) or `welder` (welded). The linking tool never fits parts. |
| `attachSeconds` | Time to fit (default 1.5 wrench, 2.5 welder); removing takes 60% of it. |
| `mass`, `health`, `armour` | kg, hit points, flat damage taken off each hit. |
| `material`, `friction` | `steel`, `rubber`, `concrete`...; friction defaults from the material (rubber 1.0, else 0.6). |
| `onBreak` | `detach` (breaks off) or `disable` (stays, stops working; the default for engines, tanks, seats, transmissions and differentials). |
| `boxes` | Up to 16: `min`/`max`, or `size` (and `at`), each with an optional `color`. `"detail": true` boxes are drawn only (no bounds, mass, collision or hit area) and may stick out. At least one box is solid. `"hidden": true`: a solid box that is never drawn. A detail box may have a `"shape"` and its settings ([Structural parts](#structural-parts-the-scale-chart)). |
| `sockets` | Up to 40 attachment points: `at` (on a face of the solid boxes' bounds), `dir` (`+x -x +y -y +z -z`, out of that face), `kind` (`struct` default, `axle`: a wheel onto a suspension, `leaf`: a door onto a hinge). Two sockets mate when their kinds match and they face each other. |
| `engine` | `torque` (N m at the peak), `maxRpm`, `idleRpm`, `peakRpm`, `curve` (`petrol`, `diesel`, `electric`, `turbine`, `flat`), `fuel` (`fuel`, `electric`, `none`), `burnRate` (litres a second at full throttle), `starter` (needs a battery), `spool` (seconds), `noise` (0 to 1). [Drivetrain parts](#drivetrain-parts-the-families). |
| `wheel` | `radius`, `grip`, `offroad` (times grip on dirt), `side` (times grip sideways), `roll` (rolling resistance), `drive` (false: a caster), `flat` (goes flat when wrecked), `track` (`length`, `rays`). Needs an `axle` socket. |
| `suspension` | `travel` (m), `frequency` (Hz, at most 6), `damping` (ratio), `maxLoad` (kg a wheel), `active` (a wired signal sets its height), `presets` (`label`, `frequency`, `damping`, `hold`). Needs an `axle` socket. |
| `transmission` | `kind` (`manual`, `sequential`, `automatic`, `cvt`, `reversing`), `gears` (reductions, lowest gear first), `labels`, `gear`, `reverse`, `shift` (s), `efficiency`; a CVT's `range`. |
| `differential` | `kind` (`open`, `locked`, `limited`, `lockable`), `ratio` (the final drive), `efficiency`. |
| `drive` | A transfer part (role `transfer`): `kind` (`shaft`, `ujoint`, `cvjoint`, `gearbox`, `chain`, `belt`, `clutch`, `transfercase`, `pto`, `hub`, `stub`), `ratio`, `efficiency`, `ports`, `maxAngle`, `maxTorque`, `front`, `setting`. |
| `fuelTank` | `capacity` (litres; a tank comes full). |
| `seat` | `at`: where the sitter's feet go. |
| `hinge` | `axis` (a direction), `pivot`, `min`/`max` (degrees), `leaf` (the leaf socket: what attaches there swings). |
| `joint` | Milestone 2, a moving joint: `type` (`motor`, `turntable`, `piston`, `steering`), `axis`, `pivot`, `min`/`max` (degrees; a piston's are metres, `min` 0 and `max` its travel up to 4 m; a turntable without them turns forever), `leaf` (a struct socket: anything fitted there moves, a door's `leaf` socket too), `speed` (degrees or metres a second), `strength` (N m, or N for a piston). |
| `logic` | Milestone 2, a logic part: `kind` (below), and for the kinds with a setting `settings` (the values it cycles through), `labels` (what the HUD shows; an input reader's are its keys), `setting` (the starting index), `settingName`. |
| `group`, `section` | The inventory's group and section for it (each up to 32 characters; a section needs a group). Without them it goes by its role. |
| `scrap` | Milestone 3: the scrap salvaging it gives once wrecked (0 to 10,000). Without it: from its mass and material ([Salvage](#salvage-and-the-inventory-milestone-3)). |
| `config` | The part's settings schema (up to 8), the configure tool's panel ([The configure tool](#the-configure-tool)). |

Milestone 1 shipped 13 parts: Frame Block, Frame Beam, Panel, Corner, Seat, Wheel, Suspension, Engine, Fuel Tank, Fuel Line,
Hinge, Door and Foundation. Milestone 2 adds 25: Deck Plate; Motor Hinge, Turntable, Piston, Steering Pivot; Transmission,
Differential; Button, Lever, Switch, Pressure Plate, Input Reader, Speed Sensor, Proximity Sensor, AND Gate, OR Gate, NOT
Gate, Timer, Delay, Toggle, Compare, Scale, Lamp, Horn and Brake. Milestone 3's weapons add 25 (in `Constructs/Parts/Weapons`:
three receivers, thirteen gun parts, five heavy guns, two ammo boxes and the feed chute) and the Wood Panel.

### Blueprints: `Constructs/Blueprints/*.json`

A blueprint is a **build script**: each step fits a part onto a socket of an earlier step's part, through exactly the
same rules a player builds by, so a blueprint that loads proves the rules can build it.

```json
{ "format": "call-of-slop-construct-blueprint/1", "name": "Gate", "steps": [
  { "part": "Foundation" },
  { "part": "Frame Block", "on": 0, "socket": "top01", "by": "-y" },
  { "part": "Hinge", "on": 1, "socket": "+x", "by": "base" },
  { "part": "Door", "on": 2, "socket": "leaf", "by": "hang", "spin": 0 }
] }
```

A step may carry `"setting": n` (a logic part's starting setting) and `"config": {"period": 2.5, "colour": "#ff2010",
"startOn": true, "setting": "jump"}` (its settings by key: numbers, true/false, colours, option names), and a blueprint may list `"wires": [[from step, to
step], ...]`, each checked by the linking tool's rules. Shipped: **Buggy** (23 parts: rear-wheel drive through a
transmission and a differential, front wheels on steering pivots), **Wired Buggy** (30 parts, 6 wires), **Gate** (6
parts), **Motor Gate** (8 parts, 2 wires), **Lift** (7 parts, 3 wires) and **Turret** (6 parts, 1 wire); milestone 3:
**Gunner Turret** (9 parts, 1 wire), **Hood Gun Buggy** (26 parts, 2 wires), **Sentry** (7 parts, 1 wire), **Gun Bench**
(9 parts, 1 wire), **Target Wall** (8 parts) and **Ammo Dump** (3 parts); the drivetrain families: **4x4 Truck** (38
parts), **Tracked Crawler** (18), **Racer** (24) and **Electric Cart** (23), and the buggies rebuilt on the structural
parts (the Buggy 26 parts, the Wired Buggy 34, the Hood Gun Buggy 27; [Drivetrain parts](#drivetrain-parts-the-families)).

### The map's `"sandbox"` section

```json
"sandbox": {
  "rack": [-22, 23, 180],
  "rackParts": ["Frame Block", "Frame Beam", "Seat", "Wheel"],
  "constructs": [ { "blueprint": "Buggy", "at": [-8, 4], "yaw": 0 } ],
  "loose": [ { "part": "Wheel", "at": [-14, 20] } ]
}
```

`rack` is `[x, z, yaw]`; `rackParts` defaults to every part; `"partsMenu": false` turns the inventory off (it is
on by default). Milestone 3: `"infiniteParts": false` makes the rack and the parts menu hand out only what the player's
inventory holds (on by default, so Construct Yard stays endless); `"debrisSeconds"` (default 180, 0 to 3600, 0: never)
and `"debrisNear"` (metres, default 12) set when wrecks are cleared away. Unknown parts or blueprints, and bad values,
refuse the map with the reason.
Only the server reads the section (clients get every construct from the server), so it is not part of the fingerprint.
The Sandbox mode is offered only on maps with this section.

## Network (protocol 82; 84 for free placement; 86 for the drivetrain's rpm; 87 for glass shattering; 88 for the physics gun)

- `Msg.CBody` (reliable): a construct's whole structure (parts on their 1 cm grid with their logic setting and signal,
  links, joints with their type, lock and wired flags, wires, pose: about 20 bytes plus 14 per part and 2 per wire),
  sent when it changes (an edit, a break, a split, a wire, a setting) and to each joiner; also part health batches, a
  construct gone, and "clear all" at a new match.
- Protocol 81 (salvage): `CBody` also carries the construct's **owner** (a player id, 255: nobody's) and a **damaged**
  flag; `CBodyKind.Inventory` sends one player their own inventory, whole, when it changes and when they join (a flags
  byte (infinite parts), the scrap, then each part kind held as id and count, then each material held; about 8 bytes plus
  3 a kind). Nobody else's inventory is ever sent.
- Protocol 82 (construct weapons): `CBody` ends with the parts holding rounds or on fire (part, rounds, a burning /
  reloading flag: 4 bytes each); `CBodyKind.Ammo` batches the changes (a box's rounds, a gun's load, a part catching fire
  or going out: 6 bytes each); `Msg.CFire` (reliable, only on ticks a gun fires) carries what the guns did that tick: each
  round (construct id, gun part, where it ended, what it met: 17 bytes) and each blast or cook-off (where, how big: 14
  bytes). Clients draw the tracers from their own copy of the gun's muzzle.
- Protocol 83 (links and power): `CBody` also carries the construct's charge (kJ), a power-flags byte (electrical, power
  on, engine running, starter failed) and its gear; `CBodyKind.Links` sends every coupling (construct and part at each
  end, the chain's length) and every wire between constructs (construct and part at each end), whole, whenever one is
  made, broken or renumbered, and to each joiner; `CPose` adds 5 bytes a body (charge share, power flags, gear, fuel);
  `CAct.Wire` carries the construct the wire goes into (0: the same one).
- `Msg.CPose` (unreliable sequenced, 20 Hz): the bodies that move (32 bytes plus 4 per joint, the angle and speed, plus
  a byte per signal part when it has logic), any whose signals changed (a lamp switched on in an anchored base), plus
  four sleeping ones in turn so a lost packet never leaves one wrong for long. Clients blend between poses and fit their
  wheels to the ground themselves.
- `Msg.CAct` (reliable): a client's request (tool, attach, place, remove, cancel, use, stand, rack, drop, pick up; wire,
  unwire, a setting, a seated player's fire/aim/reload keys; protocol 80: `SeatLook`, a seated player's seat-relative
  look, sent unreliably); the server re-checks everything and answers a refusal with its reason. Protocol 79: `Rack` carries a pick byte (0: the rack's next kind, at the rack; n: part id n - 1, from the
  inventory, anywhere) and `Blueprint` (a blueprint index) builds one in front of the player.
- The Sandbox mode-state tail (~10 Hz, version 3): every player's tool (the linking tool and the physics gun too), held
  part, seat and edit in progress, and (version 3) the construct the physics gun holds and the grab point.
- Protocol 90 (routed links): `CBody` carries the construct's routed links in place of its wires (per link a type and
  style byte, its ends and ports, its junction, its nodes on the 1 cm grid: 9 bytes plus 8 a node and a health byte a
  segment); the client rebuilds the logic's wires from the data links. `CActKind.Route` (a whole link as the client
  routed it) and `CActKind.RouteEdit` (delete, move a node, repair). Part ids are two bytes everywhere (`CBody` parts,
  stores, the inventory, the mode-state tail's held part, `CAct.Rack`'s pick).
- Protocol 88 (the physics gun): `CBody`'s flags carry frozen (32) and a byte after the owner says who froze it;
  `CActKind` Grab, GrabSteer, GrabRelease, GrabFreeze, UnfreezeAll ([The physics gun](#the-physics-gun)).
- Protocol 89 (the configure tool): `CBody` ends with the part settings that are off their defaults (a u16 count, then
  part, slot and an f32 value: 6 bytes each; a choice a part already had, like a gear, stays its setting byte);
  `CActKind.Configure` (u16 construct, u8 part count, the parts, u8 setting count, slot and f32 value each) sets them on
  one or more parts of one kind; the tool id `ConstructTool.Configure` (4).

### Joint collision

Board item dev-sandbox-joint-collision-jc01 (a hinged beam sank into the terrain: only the body's own hull touched
anything, the segments beyond a joint were posed from the joint value and went through whatever was there). Now:

- Every segment has **contact points** (`Construct.SegPoints`, in the segment's rest frame: each part's bounds corners,
  plus points every metre along a long part's edges so a beam can't sink in the middle; at most 64 a construct) and its
  **long edges** (`SegEdges`, 0.3 m and longer, at most 48). Raycast wheels on a steering pivot still roll on their rays.
- Each point is probed like a hull point (a ray down, a ray along its motion), and moves with the body *and* with every
  joint up its segment's chain. Each long edge is cast where it is and where it will be through the tick (up to four
  steps when it sweeps far); where one meets something, the point of the edge in the middle of what it met gets a ray
  along its motion too, so a post that fits between two points still stops the part. Against other constructs and
  wrecks the points test the other's part boxes (overlaps, and for a point moving faster than 3 m/s a ray along this
  tick's travel), and a still construct's hull points test the moving segments' boxes.
- **The solve** works on generalised velocities: the body's six and one per joint. A contact on a segment carries, for
  each joint up its chain, the velocity a unit of joint speed gives its point; its impulse moves the body (if it is free)
  and turns or slides each of those joints by its inverse inertia. A contact between two constructs does that on both
  sides. The **motors and limits** are constraints in the same sequential-impulse loop (a motor's impulse clamped to its
  strength a tick, a limit one-sided and speculative), solved before the contacts in each pass so non-penetration has
  the last word. So a passive hinge rests on the ground; a motor pushing into a wall stalls at its strength; a strong
  motor driving an arm into the ground lifts its free construct (the arm becomes a leg).
  A locked hinge has no give: its contacts move the body instead.
- **No tunnelling**: every probe is speculative (approach is allowed up to the gap, not past it) and covers the whole
  tick's travel, using the speed a motor can reach this tick, so a stalled arm keeps seeing what it pushes against. A
  door swung at 25 rad/s (25 m/s at its edge) stops at a 5 cm wall. Joints are capped at 25 rad/s (or m/s).
- **Why reduced coordinates still**: the joint stays one number, so the pieces can never drift apart, stretch or explode,
  and it stays the cheapest thing to send (`CPose` already carries each joint's angle and speed; no wire change,
  protocol 79). Contacts drive the joint value directly, which is what constraint-linked bodies would have to work hard
  to match. The approximation: each joint's inertia is its carried mass about its axis (as the motors already used) and
  the body's and joints' masses are treated as independent in the solve, which only shares out how a push moves things,
  never whether a contact holds.
- Not yet: a segment against its **own** construct (joint limits keep the built ones apart), players pushing a moving
  segment back (they are pushed out of it, as before), the body's mass properties following its joints.

## Salvage and the inventory (milestone 3)

Decision 7, the loot loop: what you salvage is what survives, so disabling an enemy by shooting its cheap exposed parts
leaves more to take. Board item dev-sandbox-m3, part 2 of 4 (the developer approved it 2026-10-29).

- **The inventory.** Every player has one on the server (`SandboxInventory`, Source/Constructs/ConstructInventory.cs):
  a count of each part kind (by part id, up to 9,999 each), their **scrap** (up to 999,999), and room for **materials**
  (resources come later; always empty now, but the wire carries them). Each player's client hears its own whole
  inventory when it changes (protocol 81), so the HUD and the inventory window show it; nobody else's is sent. It resets
  with the match like the constructs (nothing is saved yet). It survives death; a leaving player's empties (their id may
  be someone else's next). The read API: `Game.SandboxInventory(playerId)` (counts in `Parts[partId]` or `Count(id)`,
  `Scrap`, `Materials`, `TotalParts`, `Kinds`, `Infinite`, and `Version`, which goes up on every change so a window can
  rebuild its text only then). On a client only the local player's is known.
- **Owners.** A construct a player starts (sets its first part down, or spawns from the parts menu) is **theirs**, and
  what is built onto it stays theirs; the map's constructs are nobody's. A piece that falls away keeps its owner. A
  leaving player's constructs become nobody's.
- **Salvage.** With the tool the part needs (wrench or welder) out, hold aim on a part: the same timed removal as taking
  a part off (60% of its fitting time), but it goes **into the inventory**, not the hand. Anyone may salvage a **wreck**
  (a piece cut off, or a broken-off part), a **loose part**, a construct **nobody owns**, or someone else's construct
  once it is **disabled** (any part of it wrecked: broken off or stopped working; `Construct.Damaged`). Someone else's
  working construct is refused: "that construct is someone else's: disable it first (shoot a part off)". Nobody may take
  parts off while someone drives it. The look line says "hold aim to salvage it" (or "for N scrap", or "someone else's:
  disable it to salvage").
- **Scrap.** A part that was destroyed (a wrecked part: broken off as debris, or stopped working in place) gives
  **scrap** instead of itself: its file's `scrap`, else from its mass and material: 0.2 a kg of steel (a 120 kg engine:
  24, a 3 kg logic part: 1), 0.1 for rubber or wood, 0.05 for concrete, 0.15 for anything else, at least 1.
- **Your own constructs.** With infinite parts on (Construct Yard), taking a part off your own construct puts it in your
  hand as before. With them off, it goes back into the inventory.
- **Building from the inventory** (the map's `"infiniteParts": false`). The rack hands out the next of its kinds that
  your inventory holds, and the parts menu only a part you hold ("no Piston in your inventory (salvage one)"); either
  takes one out of the inventory into your hands (what you held goes back first). Fitting or setting it down uses it,
  and the next of the same kind comes out into your hand at once, so a row of them needs no trips back. Dropping (T)
  sets the part down as a loose part (picked up again with F); dying puts a held part back into the inventory. With
  infinite parts on the rack and the menu stay endless and nothing is counted.
- **The HUD** while holding a part shows its count ("Wheel: ... 3 more in your inventory", "the last one in your
  inventory", or "Inventory: 3 (parts are endless here)"), and a line with the totals ("Inventory: 7 parts (4 kinds), 25
  scrap") whenever the inventory holds anything.
- **Debris expiry.** A **wreck** (a piece cut off, or a broken-off part) is cleared away once nobody has been within
  `debrisNear` metres of it (12 by default; or its own size) and nobody has been taking a part off it for `debrisSeconds`
  (180 by default; 0: never), checked every half second. Constructs players build and loose parts they set down never
  expire. The cap on bodies (64 in all) still stops new debris appearing when the world is full.

Choices made (the developer may change them): salvage always goes into the inventory, even with infinite parts on (so
the loot loop and the counts work on the yard); a construct counts as **disabled** as soon as any of its parts is
wrecked (there is no repair yet, so it stays salvageable); after a fit the next of the same kind comes into the hand
automatically; the debris defaults (3 minutes, 12 m); inventories survive death but not leaving or a new match; the
scrap rates by material.

## Physical storage (decision 18)

The developer's decision 18 (2026-10-29, "Inventories"; board item dev-sandbox-physical-storage-ps02): creative keeps the
inventory window (above); survival keeps things physical. Storage parts are construct parts with **slots** you set parts
into in the world, and bins that hold bulk material with a visible fill level. There is no storage screen: the only UI is
the look-at tooltip. It works on every Sandbox map (the parts are parts), so the yard's creative players can use it too.

### The parts (`Constructs/Parts/Storage`)

- **Chest** (wrench): two medium slots inside an open steel box. The **Chest** blueprint adds a **Lid Hinge** (a long,
  flat motorised hinge, `power` 0) on its back edge and a **Chest Lid** on that: F on the lid opens and shuts it. Shut,
  the lid is in the way: nobody sees into the chest or takes anything out (the look ray meets the lid).
- **Parts Shelf** (welder): two big slots below (an engine fits), three **parts bins** above, each piling up to six of
  one small part (a pile of lamps, of buttons...).
- **Wheel Rack** (welder): three slots that take only wheels, standing upright (a slot's shape).
- **Scrap Bin** (welder): an open bin of 500 **scrap**; its level rises with what is in it.
- **Scrap Bundle**: what a broken bin spills (wrecked: salvaged with the wrench for 50 scrap).
- Blueprint **Storage Corner**: a foundation with a shelf and a bin on it (anchored).

More storage (crates, tool and gun racks, pegboards, cargo beds) is data: a part with `"role": "storage"` and a
`"storage"` block.

```json
"storage": {
  "slots": [
    { "min": [-0.405, 0.045, -0.255], "max": [-0.005, 0.545, 0.255] },
    { "min": [-0.51, 0.06, -0.42], "max": [-0.19, 0.88, 0.42], "fits": ["wheel"], "label": "wheel" },
    { "min": [-0.56, 0.64, -0.26], "max": [-0.2, 1.16, 0.26], "stack": 6, "label": "parts bin" }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `slots` | Up to 12 boxes in the part's frame (`min`/`max`, or `size` and `at`), at least 5 cm a side, inside the part, clear of its solid boxes and of each other: the room a stored part takes. |
| `fits` | Optional: only parts of these roles (`wheel`, `engine`, `gun`...): a slot of a **shape**. Without it, any part whose box fits. |
| `stack` | 1 to 12: how many of one kind pile up in it (a bin of small loose items). |
| `label` | What the tooltip calls the slot ("wheel", "parts bin"); without it the **size class** from its longest side: small (up to 35 cm), medium (up to 70 cm), large. |
| `bulk` | Instead of slots: `material` (`scrap`; more materials come with resources), `capacity` (up to 60,000) and the fill box (`min`, `max`) its contents rise in. |

A part **fits** a slot when its box goes inside the slot's in one of its 24 turns (upright turns are tried first, no
turn first); it sits centred on the slot's floor in that turn. A pile stacks its copies up as far as the slot's height
allows (at least one drawn; the count is in the tooltip). A bad block is refused with the reason
(`--check-construct-parts`).

### Using it

- **Putting a part away**: carry it (from the rack, the parts menu, picked up, taken off with a tool), look at a storage
  part within reach (3.2 m): every slot it fits lights up as a **ghost outline** (blue), the one nearest the crosshair
  green with the part shown where it would sit. **Click** sets it in, at once, with any hands (a tool out or not). A part
  carried in both hands no longer fires the gun.
- **Taking it out**: look at a stored part and press **F**: it comes into your (empty) hands. Anyone may: stored parts
  can be stolen. A pile gives one at a time.
- **A bin**: F tips in all the scrap you carry (as much as fits); with none on you, F takes a load of 100 out.
- **The tooltip** (the look line): "Chest: 1/2 slots used (1 part), 2 medium slots - click a lit slot to set the Lamp
  in"; "Lamp x4 (6 fit) in the Parts Shelf, 100% - F to take one"; "Scrap Bin: 230/500 scrap - F: tip in your 40 scrap".
- **Damage**: a stored part is hit like any part (bullets, construct guns, blasts: a blast on the container hits what is
  in it); its armour counts, and at zero health it is destroyed. A container that goes (broken off, taken off, wrecked)
  **spills** what it held where it was, as loose parts anyone may pick up (a pile as many as there were, while the caps
  on bodies allow; the rest is lost and logged), and a bin's scrap as up to six scrap bundles. A loose container still
  holding something can't be picked up ("empty the Chest first").
- **Survival salvage**: on a map with `"infiniteParts": false` and `"partsMenu": false` (survival) nothing is counted
  abstractly: a part salvaged (or taken off your own construct) comes into your **hands**, or is set down where it came
  off when they are full, ready to be carried to storage. A wrecked part still gives scrap, which counts on the player
  (until carrying is decided) and goes into bins. Creative maps keep the counted inventory as before.

### Server, network and state (protocol 85)

- Each storage part holds a handle (`CPart.Store`) into the world's pool of stores (`ConstructWorld.Stores`, 128 of them,
  allocated once): per slot the stored part's type, its turn, how many and the top one's health; a bin's amount. The
  handle moves with the part when constructs split or renumber, so nothing is copied.
- The server decides every change (`CActKind.StoreIn`, `TakeStored`, `Bin`; checked again: reach, the hands, the fit).
  Two players taking the same part: the first request the server handles wins; the other is told "that slot is empty".
- A construct's structure lists its storage parts' handles; **`CBodyKind.Store`** sends one container's contents when
  they change (four bytes a slot, or two for a bin), after the structures in the same tick; a joiner gets every live
  store. Bad values are refused.
- `ConstructStorage.Write` / `Read` are the state's bytes (the same as the wire's): a world save, when there is one, will
  write them as they are. Nothing is saved yet (constructs reset with the match).
- Drawing: each stored part's boxes are drawn with its container (its segment's pose: a store on a turntable turns with
  it), a bin's fill as a box with lumps on top; nothing allocates per frame or per tick (the fit check the look runs each
  frame returns a code, not text; the tooltip is rebuilt only when what it shows changes).

### Self-test and pictures

The **Construct storage** section (`Source/App/SelfTestConstructStorage.cs`): the storage parts load with their slots and
ten kinds of bad block are refused for their reason; what fits (a suspension in a chest slot, not an engine; an engine on
the shelf; a wheel in the rack, not a suspension; six lamps pile, the seventh and a button refused); the state's bytes
read back the same (a truncated or unknown one refused); in an offline game on the yard the shut lid hides the chest's
slot and F opens it, a suspension set in is stored, sits inside its slot and a ray finds it there, an engine is refused
and stays in the hands, a taken slot is refused, taking out returns it to the hands, two players taking one part: the
first wins, a stored lamp shot to pieces goes (the chest untouched), the shelf piles six lamps and refuses a button, a
loose stocked shelf can't be picked up, the bin takes 120 scrap and gives 100 back with its level rising, a destroyed
chest spills its suspension and battery as loose parts and frees its store, a broken bin spills six bundles, and in
survival the gate's door salvaged comes into the hands; no allocation in 120 ticks of stocked storage, rays into the
slots, fit checks, takes and sets and the store's message; and a real host with two clients: a client's part goes in,
both clients see the contents, both grab the same part and exactly one gets it, and a late joiner sees the contents.

`--storage-shots` (Program.StorageShots.cs) writes `smoke_storage_*.bmp` under xvfb: the row of storage empty and
stocked, the chest shut and open, the slot outlines for a carried lamp with the tooltip, the stocked shelf, the wheel
rack, the bin half full and full, and a destroyed chest's contents spilled.

### The backpack

The developer's answer on carrying (2026-10-30): the hands plus a visible backpack of about eight slots of different
sizes, still no inventory screen. Every Sandbox player wears one (`ConstructEdits.Packs`, Source/Constructs/ConstructBackpack.cs):

- **The layout is data**: the **Backpack** storage part (`Constructs/Parts/Storage/Backpack.json`): a pack frame with
  four small pouches, a big bag, two side pouches and a hip pouch, and `"maxMass": 40` (a storage block's new optional
  limit: no part heavier than that goes in). A part goes in when its box fits a slot (as for any storage) and it weighs
  at most 40 kg: a lamp, a battery, a suspension, a gun barrel do; an engine is too heavy, a wheel too big, and storage
  parts never go inside storage. As a part, the backpack also works as storage bolted onto a construct.
- **Keys** (rebindable; binds version 11, given to older files when the key is free): **Stow** (H) puts the part in
  your hands into the smallest free slot it fits ("the Engine is too heavy for the backpack (120 kg; it takes 40)", "the
  Wheel is too big for the backpack (carry it in your hands)", "no free backpack slot takes the Battery"); **Draw** (J)
  takes the next part out into empty hands, round the slots from the last one drawn. Not while seated. The hands still
  carry one part, as before.
- **Seen**: on every player's back in third person (the pack frame and the parts in their slots; `BackpackRender`,
  the immediate path and the scene graph's `PackNode` emit the same boxes), and in first person as a strip of the slot
  outlines at the bottom of the screen (`UiSandboxPack.cs`), each square sized by its class with its letter (S, M) and the
  part in it as a tiny 3D tile from the inventory's atlas (a colour swatch until the tile is made). The HUD line says
  "Backpack 2/8: Suspension, Lamp (H: stow, J: draw)" while it holds anything.
- **Server and network**: the server decides (`CActKind.Stow`, `CActKind.Draw`); a changed pack goes to every client
  (`CBodyKind.Pack`: the player id and the store's bytes, the same format as storage) and a joiner gets every pack that
  holds anything. Packs empty with a new match.
- **Death**: the pack's contents drop round the body as loose parts anyone may pick up (the hands' part goes back as
  before); a player who leaves drops theirs too. Chosen over keeping them: survival stays physical, and what a player
  carried can be looted.

The **Sandbox backpack** section (`Source/App/SelfTestSandboxBackpack.cs`): the layout (eight slots, three kinds, a weight
limit) and the keys (unshared, migrated from binds version 10); in an offline game a lamp stows into a small slot and a
suspension into the big bag, an engine is refused as too heavy and a wheel as too big (both stay in the hands), drawing
is refused with full hands, takes both parts out in turn, then says the pack is empty; the pack is drawn behind the
player with its parts, the HUD line and the eight-slot strip show (headless, no ImGui error, nothing allocated per
frame); the pack's message and drawing allocate nothing; a death drops both parts near the body and empties it; and a
real host with two clients: a client's stow reaches the server, its own copy, the other client and a late joiner.
`--storage-shots` adds `smoke_storage_backpack_*.bmp` (from behind, from the side, the first-person strip).

### Choices made (the developer may change them)

- **Carrying**: one part in the hands plus the backpack above (eight slots, 40 kg a part); no belt yet.
- **The backpack** drops its contents on death (rather than keeping them); stowing picks the smallest slot that fits;
  drawing goes round the slots; keys H and J.
- Storage works on every Sandbox map, creative too (alongside the inventory window).
- Setting in and taking out are **instant** (no tool, no timer); clicking with any hands.
- Fitting is by the part's **box** (it must physically fit in some turn), plus an optional role filter for shapes;
  smaller parts fit bigger slots.
- The chest lid is a **motorised** hinge (a passive hinge's latch can't hold a lid up against gravity); like other
  motors it needs power on a map that requires it.
- **Survival salvage** goes to the hands (or is set down when they are full); scrap still counts on the player, and bins
  store it (F tips all in, or takes 100 out). Scrap is the only bulk material until resources exist.
- Stored parts add **no mass** to their construct yet, and keep only the top one's health in a pile (a part taken out
  comes out whole, as the hands carry no health).
- A spilled pile becomes as many loose parts as the caps allow; a broken bin's scrap becomes at most six bundles of 50.
- A loose container holding anything can't be picked up; a construct that is cleared away (debris expiry) takes its
  contents with it.

## Construct weapons (milestone 3)

Decision 10: construct weapon parts like the Zombies weapon builder's, but assembled in the world into fixed or turreted
guns on vehicles and bases. Board item dev-sandbox-m3, part 1 of 4 (the developer approved it 2026-10-29). Everything is
a part fitted with the **wrench** like any other (no workbench, decision 2), and every part's weight counts.

### The parts (`Constructs/Parts/Weapons`)

- **Guns on a weapon builder receiver** (`"gun": { "receiver": ..., "action": ... }`): **Rifle Receiver** (the
  Workhorse, full auto), **Heavy Receiver** (the Thumper chain gun, heavy calibre) and **Shell Receiver** (the Street
  Sweeper). Each is a 20-26 cm block with six sockets: **front** (a barrel or a splitter), **left** (a feed), **right**
  (ammo), **top** (an optic), **bottom** (its mount) and **back**. The gun parts bolted to it are put together as the
  weapon builder would (a `BuildNode` tree: its action, the feed, the ammo, the barrel or splitter with its barrels, a
  muzzle device on each barrel's front) and checked by the weapon builder's own socket rules (`BuildRules.SocketWhy`: a
  shell feed on a rifle receiver is refused with the workbench's reason); its numbers come from the weapon builder's
  stat pipeline (`BuildStats.ReceiverStats`: damage, fire rate, magazine, reload, spread, range, pellets, its flags).
- **Gun parts** (`"gunPart": { "part": ... }`, a weapon builder part by name): **Gun Barrel** (Standard Barrel), **Long
  Gun Barrel**, **Twin Splitter** (Y-Splitter: two front sockets for two barrels), **Box Magazine** (30, rifle), **Belt
  Feed** (150, rifle or heavy), **Shell Feed**, **FMJ / AP / HE / Incendiary Rounds** (FMJ, Armour Piercing, Explosive
  Tip, Dragon's Breath), **Gun Muzzle Brake**, **Gun Suppressor** and **Gun Scope** (4x Scope: a gun with an optic has
  60% of its spread). A receiver with no ammo part fires FMJ.
- **Heavy construct guns** (their own numbers in the `"gun"` block; original designs): **Mounted MG** (24 damage, 600
  rpm, 100 a belt), **Autocannon** (explosive 30 mm shells, 150 rpm, a 1.8 m splash), **Cannon** (one 260-damage
  explosive shell, a 3.5 m splash, 3.5 s reload, a big kick back), **Rocket Pod** (six rockets, a slow smoke streak and
  a 3 m blast each) and **Flamethrower** (a 9 m gout of incendiary fire). Each mounts by its **bottom** socket, has
  sockets for a box on its back, left, right and top, and takes an ammo gun part bolted to it (the weapon builder's
  universal ammo: AP rounds make the MG penetrating, HE explosive).
- **Ammo Box** (300 rounds) and **Ammo Crate** (1,200), and the **Feed Chute** (a 0.5 m pipe, like a fuel line).
- **Wood Panel**: a cheap 1 m plank panel that burns (for targets; material `wood`).

| `gun` key | Meaning |
| --- | --- |
| `receiver`, `action` | A weapon builder receiver (its numbers come from the parts bolted to it) and its action (default: the receiver's stock action). Not with the numbers below. |
| `damage`, `rpm`, `spread`, `range`, `mag`, `reload`, `pellets` | A heavy gun's numbers (`spread` in degrees, the whole cone; `range` the full-damage range in metres). |
| `flags` | `penetrating`, `explosive`, `incendiary`. |
| `blast`, `blastDamage` | An explosive round's splash: radius (m) and damage at the centre (default: its damage). |
| `maxRange` | How far its rounds reach at all (default 300 m; the flamethrower's 9). |
| `muzzle` | Where its rounds come out (part frame; -Z is forward). |
| `kick` | The push back on a free construct a shot (N s). |
| `fx`, `soundOf` | `bullet`, `cannon`, `rocket` or `flame` (how its tracer and flash look); the weapon file whose report it uses. |

An ammo box's block: `"ammoBox": { "rounds": 300, "cookOff": { "radius": 3.5, "damage": 260 } }`. A gun part's:
`"gunPart": { "part": "Long Barrel" }` (barrels, splitters, feeds, ammo, muzzle devices and optics only).

### How a gun works

- **Its feed.** A gun is fed by the ammo boxes **bolted to it, or to any of its gun parts** (a receiver's magazine), or
  at the end of a chain of **feed chutes** (up to four boxes, the nearest first). It loads a magazine's (or a belt's)
  worth from them, taking the reload time, as soon as it is empty and a box has rounds, then fires those; a gun with no
  box fires what it has loaded and then nothing. The HUD and the look line say "no ammo box on its feed", "reloading",
  "out of ammo" or "24/30, 270 in the boxes".
- **Its trigger.** A gun is a logic **input** (it takes up to 4 wires): with anything wired into it, it fires while the
  wires add up to 0.5 or more (a switch, a button, an input reader on a driver's fire key, a proximity sensor, a timer).
  With nothing wired in, the **gunner** fires it: the sitter of a **gunner's seat** (a seat on a moving joint's segment)
  on the gun's segment or one up its chain, with their fire key. Every shot fires each barrel's pellets at once, at the
  gun's rate; semi-automatic and burst actions fire at their rate while held (no trigger discipline on a mount).
- **Its aim.** A **fixed** gun (on the body) points where its construct points. A gun on a **turntable** (and on a **motor
  hinge** for elevation) is pointed by its gunner's **look**: every turntable and motor hinge up the gun's chain turns,
  each about its own axis, until the gun points at what the gunner looks at (a ray from the gunner's eye along their
  look: the map, another construct or a player, else the gun's reach), so the rounds go where the crosshair is however
  far the gun sits from the eye. A hinge lying across the gun only tips it up and down (it never swings over the top to
  point behind; the turntable does the bearing). A seated gunner overrides the wires on those joints (the Gunner Turret's
  lever turns it when nobody sits there). The gunner's own view no longer turns with a seat that the gunner points:
  they look where they look and the turret follows.
- **A sentry.** A gun with a **proximity sensor** wired into it points its turntables and motor hinges (unwired ones)
  at the nearest player in the sensor's range and holds fire until it points within 8 degrees of them (it aims at the
  chest). The sensor's output is the trigger, so it fires only while someone is in range.
- **Rounds** are resolved at once (hitscan; a cannon shell or a rocket is drawn travelling at its `speed` but lands at
  once): the nearest of the map, a construct part (never its own construct's) and a player, bot or zombie. A part takes
  the round's damage less its armour. **Penetrating** rounds go on through the part (75% of their damage left, less 1.5
  times its armour; up to 3 parts), so an AP round through a plank panel hits the block behind it. **Explosive** rounds
  splash where they stop (every construct part in range, falling off with distance, and players at half that, walls
  shielding). **Incendiary** rounds set **flammable** parts alight (wood, rubber, fuel tanks, ammo boxes): a burning part
  burns for 12 s, losing a tenth of its health (plus 2) a second whatever its armour, and half-way sets flammable parts
  bolted to it alight. Players hit by incendiary rounds burn as from the weapon builder's guns.
- **Cook-off.** An ammo box holed to zero health (shot, blasted or burnt) blows up 0.35 s later, by how full it was
  (radius and damage from its file, half to all of it): a blast that can set off the box next to it. A **fuel tank** that
  gives way while burning goes up too (by the fuel in it).
- **Who gets the kill**: the gunner, else the player the construct belongs to (a sentry's kills are its owner's), else
  nobody (the victim's own, like being run over by a driverless car).
- **Weight**: guns, boxes and gun parts weigh what their files say, like any part (the Hood Gun Buggy is 46 kg
  heavier than the Buggy without its tow hitch), and a free construct is pushed back by each shot's `kick`.

### The yard

Construct Yard gains a **Gunner Turret** (a turntable, a gunner's seat on the arm, a Mounted MG on a motor hinge at the
front and an ammo box: sit in it, look, fire) facing a **Target Wall** (a steel panel on a post, a fuel tank and an ammo
box behind, a wood panel on the side); a **Hood Gun Buggy** (the buggy with a Mounted MG on its hood, fired by the driver's
fire key through an input reader); a **Sentry** (a Mounted MG on a turntable with a 6 m proximity sensor wired into it,
south-east, away from the spawns); a **Gun Bench** (a weapon builder gun from parts: a rifle receiver, barrel, muzzle
brake, box magazine fed by an ammo box, AP rounds and a scope, fired by a switch on its base) pointing at an **Ammo Dump**
(two crates: shoot one, and it sets off the other). Each is a blueprint in the parts menu too. The rack hands out every
weapon part. `--weapon-shots` writes pictures (`smoke_weapons_*.bmp`): the yard's guns, the gunner turret firing at the
target wall (two views), the hood gun, the sentry turned onto a player, the gun bench, the five heavy guns firing, a
cook-off, the flamethrower's fire and a burning wood panel. It runs under xvfb like `--light-shots`.

### Decisions made for construct weapons (the developer may change them)

1. **Feed by touch, not wires**: ammo boxes feed a gun when bolted to it or its gun parts, or through feed chutes, like
   fuel through fuel lines (wires stay for signals).
2. **A receiver gun is its parts on the construct**: barrel, splitter, feed, ammo, muzzle device and optic are each a
   construct part naming a weapon builder part (13 of them, a starting set; more are a file each). The action comes with
   the receiver part (its stock action, or one named in its file), not as a part of its own. Grips, stocks, upgrades and
   utility parts don't apply to a mounted gun.
3. **Rounds are hitscan** with travelling tracers (a cannon shell or rocket lands at once; players can't dodge it yet).
4. **Gunners aim at what they look at** (convergence), and a seated gunner overrides the wires on the joints they point.
5. **A sentry is a gun with a proximity sensor wired into it**; it targets any living player in range (no factions yet:
   its owner too) and only players (bots are players; zombies only when it is a gunner's gun or a credited sentry).
6. **Optics**: a gun with an optic has 60% of its spread (no zoomed gunner view yet). **Semi and burst actions** fire
   at their rate while the trigger is held.
7. Players take **half** a blast's construct damage (a cannon would otherwise one-shot from across the yard).
8. **The Gunner Turret lost its shield panel**: the motor hinge and the gun sit on the arm's front instead.

## Merged receivers

Board item dev-sandbox-receiver-merge-rm01 (the developer approved it 2026-10-30). Code: `Source/Constructs/ConstructMerge.cs`;
rules: `Constructs/ReceiverMerge.json`; self-test: "Construct merged receivers".

- **What merges.** Weapon builder receivers (Rifle Receiver, Heavy Receiver: the `gun` parts with a `receiver`) of the
  **same type**, turned the same way, on the same segment, side by side in one plane (their backs flush, on the grid of
  their own size) and forming an **NxN square** (N from `minSize`, 2, to `maxSize`, 6) are one **merged receiver** drawn
  N times bigger: its sides flush with the square's, its back with the members' backs, so it reaches N times as far
  forward. T, L and 1x4 shapes stay single receivers. A broken receiver takes no part.
- **Found greedily, largest first**: every size from the largest that could fit down to 2, each scanned in rows (the
  receivers' up, then their right); a square whose cells are all free is taken. A 3x3 inside a complete 4x4 is the 4x4;
  a 3x4 is a 3x3 and three singles. The member with the lowest part index **leads**: it is the merged gun.
- **Derived, never stored**: the merges (and the scale of their gun parts) are worked out from the parts and links with
  every rebuild, so every client and every copy of a construct finds the same ones; nothing new goes on the wire (the
  protocol is unchanged) and a saved construct would need nothing either. Finding them allocates nothing.
- **One centre socket set.** The members' own sockets take no gun parts ("that receiver is merged: fit gun parts to the
  merged receiver's centre sockets"; their structural links stay: mounts, frames, each other). The lead gets the merged
  receiver's sockets after its own (socket index `own count + s`): the receiver's sockets N times out from the merged
  origin (the barrel at the front centre, the feed on the left, ammo on the right, the optic on top...). They take gun
  parts only. Ghosts show them like any socket.
- **Scaled gun parts.** A gun part on a centre socket, and every gun part bolted on through it (a muzzle device on the
  barrel, a splitter's barrels), is drawn and simulated **N times bigger** (its boxes, sockets, hits and contact points)
  and weighs N to the `partMass` power times its file (N squared). Ammo boxes stay their size. Ammo boxes bolted to any
  member feed the merged gun; a wire into any member is its trigger.
- **Its numbers** (each a power of N in the rules file, so the developer can change them): damage N^2, fire rate / N,
  recoil (kick) N^2, rounds a shot N^2 (out of its load and boxes), magazine N^2 (the same shots a load), spread and
  range unchanged. So a 2x2 is a slow cannon worth four rifles' damage a shot at four rifles' ammo, not four rifles.
  `sameTypeOnly: false` would let mixed receivers of the same size merge (the lead's type).
- **Building** (the server settles every receiver attached or removed): when a square forms, the gun parts on its
  members' own sockets **pop off** (with ammo boxes hanging only on them) into the builder's hands, else their backpack,
  else they drop as loose parts where they were (never lost). An old merge's centre parts move
  onto the new merge that holds most of its members (its same socket, scaled to its size), so growing a 2x2 to a 3x3
  keeps the barrel; when there is no such merge, or something would be in the way, they pop off.
- **Taking apart**: the wrench takes off only the member whose own cell is under the crosshair (the outline is that
  cell, never the block); the rest merge again (a 3x3 less a corner is a 2x2 and four singles, its centre parts at 2x).
- **Damage**: each member keeps its own health and hitbox; a round damages the cell it hits; the look line shows the
  members' health together ("Rifle Receiver (2x2 merged, 620/720): ..."). A cell shot to pieces breaks the merge as
  taking it off does (its centre parts move or drop: nobody placed them).
- The merged receiver's reach beyond its members is solid to building (nothing can be fitted into it) but not to rounds
  or the world: rounds hit the members and the gun parts.

### Decisions made for merged receivers (the developer may change them)

1. **Only identical receiver types merge** (`sameTypeOnly`), turned the same way (the proposal; mixed types: the
   developer's question, not asked yet).
2. **The scaling table** is the proposal, in `Constructs/ReceiverMerge.json`: damage N^2, fire rate / N, recoil N^2,
   ammo N^2 a shot, magazine N^2, spread and range unchanged, scaled gun parts' weight N^2. N from 2 to 6.
3. **The merged receiver grows forward** (its back flush with the members'), not about their centre.
4. **Popped parts** go to the hands, then the backpack, then drop (the shared give rule); ammo boxes hanging only on them come too; with
   nobody placing (damage) they drop where they were.
5. **The members' structural links stay** (only gun parts are refused on their own sockets), and boxes bolted to any
   member feed the gun.

## Links between constructs and power (milestone 3)

Board item dev-sandbox-m3, part 3 of 4 (the developer approved it 2026-10-29). Code: `Source/Constructs/ConstructLinks.cs`
(couplings, wires between constructs), `Source/Constructs/ConstructPower.cs` (the electrical system), the coupling rows
and joint reactions in `ConstructPhysics.cs`.

### Couplings: hitches and chains

- A **hitch** part (`"role": "hitch"`, a `"hitch"` block: `kind` `ball`, `eye` or `hook`, `at` the coupling point in
  the part's frame, and for a hook its `chain` in metres) couples to a hitch on **another** construct: a ball or a hook
  to an eye. Shipped: **Tow Hitch** (a tow ball on a crossbar), **Tow Eye** (a drawbar's eye), **Chain Hook** (2.5 m of
  chain) and **Wrecking Ball** (450 kg with an eye on top).
- **Coupling and uncoupling**: with the **wrench** out, F on a hitch couples it to the nearest free hitch that fits (a
  ball's point within 0.6 m of an eye's; a hook's eye within its chain and 0.3 m) or uncouples it. Bare hands or another
  tool: "take out the wrench (3) to couple or uncouple". A coupling also goes when either hitch is **shot off** or
  wrecked, when its parts end up on one construct, or when the two are torn more than 1.5 m apart (a teleport, a blast).
- **A tow ball is a free pivot**: three rows in the contact solve (one per axis) hold the two coupling points together,
  pushing either way (no friction), so the trailer turns any way about the ball and can't come off it. **A chain** is one
  row along the line between the points that only pulls (the ball swings and drops, but never further than the chain).
  Each side moves its body and every joint up its hitch's chain, like a contact, so a chain hook on a turret's arm works.
  Coupled constructs still collide everywhere except within 0.7 m of the hitch points.
- A towed construct (coupled to one that is driving) lets go of its **parking brake**; coupled constructs wake each
  other when one moves. Couplings name their parts by a stable tag (`CPart.Tag`), so they survive other parts coming off
  either side (salvage, a split).
- The chain is drawn as links along a curve that sags when it is slack; a tow ball shows a pin.

### Wires between constructs

- The linking tool now wires **into another construct**: fire on an output, then on an input of any construct within
  **12 m** (part centre to part centre). The same rules as a wire inside one construct ("those are already wired", a
  part's input limit counts both kinds), and "an input reader reads a seat of its own construct". Aim on a part cuts
  its wires of both kinds. A wire between constructs carries its source's value from **last tick**, like every wire.
- It **breaks** when either part is gone or wrecked, or when the parts are more than 12 m apart (a towed trailer driven
  off, a wall button's post carried away). Drawn with the linking tool out, orange while off.
- A map can wire its starting constructs (below): the yard's **Wall Button** runs the **Motor Gate**.

### Power

- A construct with a **Battery** part (`"battery": {"capacity": kJ}`; it comes charged, 300 kJ) is **electrical**: its
  electrical parts work only while a **working battery's links reach them** (the frame is the circuit: a wrecked part
  breaks it, so shooting the battery or the beam between cuts the power) and the batteries have charge. Electrical
  parts: every logic part (with no power they put out 0), every moving joint's motor except steering pivots (a motor
  with no power lets go: a lift sinks, a motor gate hangs loose), and anything with a `"power"` draw.
- **The draw** (watts, `"power"` in a part file): a part draws while its signal is on (lamps 60 W, horns 120 W, other
  logic parts 2 W); a motor draws 10 W while it moves plus its work (torque times speed) over 0.8. `Construct.PowerUse`
  is last tick's total. A flat battery comes back on at 0.2 kJ (no flicker at empty).
- **The engine needs the starter**: asked to go (the driver's throttle, or a signal into it) an electrical construct's
  engine takes 2.5 kJ to start (the engine must be on the battery's circuit); flat: "It won't start: the battery is flat".
  It runs until nobody asks it to. An **Alternator** touching the engine charges the batteries while it runs (1.5 kW).
- **Optional per map** (decision): a construct without a battery runs as milestone 2 did (nothing needs power), so every
  earlier blueprint keeps working; a map's `"power": "required"` makes every construct electrical (no battery: no logic,
  no motors, no start). The Wired Buggy (battery, alternator, charge gauge), the Motor Gate (battery, charge gauge) and
  the Lamp Post carry batteries; the Buggy, Gate, Lift and Turret don't.
- The **HUD** while driving shows the gear and the charge ("charge 84%", "(flat)", "engine off"); the look line shows a
  battery's charge and "(no power)" on an electrical part without it.
- Construct guns don't draw power (yet): a gun part could, by giving it a `"power"` draw and checking
  `ConstructPower.On(construct, part)`.

### Joint reactions

A moving joint's motor and its stops push back on the body it is mounted on: each impulse the solve gives the joint, the
body takes back (a turn about the joint's axis, or a piston's push at its pivot), through the inertia of what doesn't
turn on the joint (`Construct.InvInertiaBaseLocal`: the body's own parts, and each jointed piece as its mass at its
pivot). A turret swung hard rocks a light buggy; a turntable under a block hanging on a chain turns the block the other
way. Steering pivots don't (they are the driver's hands on a rack). `ConstructPhysics.JointReactions` turns it off (the
self-test compares).

### Gears

A transmission may have `"transmission": {"gears": [ratios], "labels": [...], "gear": n}`. Since the drivetrain families
a ratio is a **reduction** (the engine turns that many times for each turn out, the lowest gear first) and the engine has
a torque curve and a limiter ([Drivetrain parts](#drivetrain-parts-the-families)); the shipped Transmission has three
(4.4, 3.0, 2.1; it starts in 2). The linking tool's R picks its gear; a signal wired in (one input) picks it instead
(1 the first gear, rounded; below 0.5 the R gear). The **Gear Selector** (logic kind `selector`) steps 1, 2, 3, 1... on
F or a rising input (an input reader on the driver's reload key makes a gear shift).

### More parts

**Bumper** (rubber-faced bar, armour 10, 500 health), **Fender** (a mudguard panel), **Speed Gauge**, **Fuel Gauge** and
**Charge Gauge** (logic sensors with a **dial**: `"dial": {"at", "face", "max"}` draws a needle swept 270 degrees by the
signal; the fuel and charge gauges put out 0 to 1, in hundredths), the **Gear Selector**, Battery, Alternator and the
hitches above. 12 new part files; 50 in all.

### The map's links and the yard

The `"sandbox"` section takes `"power": "optional" | "required"`, `"couplings": [{"hitch": [construct, step], "eye":
[construct, step]}]` (the second construct is moved so its eye meets the ball) and `"wires": [{"from": [construct,
step], "to": [construct, step]}]`, constructs by their index in `"constructs"` and parts by blueprint step. Construct
Yard adds a **Trailer** coupled to the Buggy (which now has a Tow Hitch), a **Wall Button** at (-6, -14) wired into the
Motor Gate's toggle, and a battery **Lamp Post** at (-13, -5) that lights while someone is within 6 m. (They are listed
after the weapons' constructs, so the map's couplings and wires name constructs 12, 13 and 14 and the Motor Gate, 3.)

### Self-test and pictures

The **Construct links and power** section (`Source/App/SelfTestConstructLinks.cs`): 16 bad hitch, battery, gauge, gear
and sandbox-section files refused for the right reason; the buggy tows the trailer 19 m in 3 s and round a 90 degree
turn with the coupling points within 1 cm; bare hands refused, the wrench uncouples (the buggy drives off alone) and
couples again, the hitch shot off; a wrecking ball swings on its chain and never gets further than 2.5 m from the hook;
a wall button wired into the Motor Gate opens it, the wire's refusals, and the wire breaks when the button is carried
out of range; a flat battery stops the Motor Gate's motor, the wired buggy's lamps and its starter, a little charge
starts it and the alternator charges it, a shot battery cuts the power, and a map that requires power stops a buggy with
no battery; a turntable under a block hanging on the chain turns the block back (not with reactions off); gear 1 pulls
harder and tops out at 11.4 m/s while gear 3 goes faster, and a gear selector picks gears 1, 2, 3; the yard's trailer,
wall button wire and lamp post; a client's copy of the links, the charge and the gear, a truncated links packet
refused; no allocation in 120 ticks of all of it. "Constructs"' loopback also checks a client and a late joiner mirror
the couplings, the wires between constructs and the power. `--power-shots` (Program.PowerShots.cs) writes
`smoke_power_*.bmp`: the trailer, its hitch, towed round a turn, the wall button's wire and the gate it opened, the lamp
post, the gauges, the driving HUD and the wrecking ball.

### Choices made (the developer may change them)

1. Power is **optional per construct**: no battery means milestone 2's free power, unless the map says `"required"`.
2. The **frame is the circuit** (no wire parts): power reaches whatever a working battery's links reach.
3. Steering pivots and the engine's drive don't use the battery (only the starter does); a running engine charges only
   through an alternator touching it.
4. Wires between constructs reach **12 m**; couplings carry no power or signals.
5. Coupling is **instant** with the wrench (F), not timed; a rigid coupling made up to 0.6 m apart pulls together.
6. Joint reactions use the base's inertia (what hangs on a joint counts as its mass at the pivot); the body's mass
   properties still use the rest pose.
7. Gears: three in the shipped transmission, the second as before; no reverse gear (S still reverses).

## Structural parts (the scale chart)

The developer's scale chart (2026-10-29, "Realistic"): a player is 1.8 m, a buggy 4.0 by 1.8 m, a truck 8 m, a crawler
7 m; **frame step 0.25 m** (beams and plates 0.25 to 2 m); wheels 0.2 to 1.2 m radius; engines 0.4 to 1.0 m; the 1 cm
grid and the 24 turns unchanged. The structural parts are written by `tools/constructs/structure_parts.py` into
`Constructs/Parts/Structure` (110 files; run it again after changing a family, `--check` compares) and are plain part
files like any other.

**The cell rules** (the "Structural parts" self-test checks every part in every turn):

- A part is made of 0.25 m cells, and its origin is the centre of one of them. Every socket sits on a cell face, in the
  middle of the cell's face. Out of the face a socket is half a centimetre off the whole ones (12.5 cm from a cell
  centre; a thin plate's front, a rod's side and a cap's front likewise: thicknesses are whole centimetres, rods and
  tubes 5 and 11 cm across), along the face on a cell centre. So two sockets that meet always put the new part's
  origin on a whole centimetre, and its cells carry on the old part's 0.25 m lattice.
- A thin plate lies on the face of its cells (its back on the cell face): a plate on a beam sits flush on it, a plate
  on a plate stacks. Its edge sockets are 1 cm into the plate, so a plate edge welded to a beam's side sits on the
  beam's middle.
- The old big pieces (Frame Block, Frame Beam, Panel, Corner, Deck Plate, Wood Panel, Bumper, Fender) keep their sizes
  and sockets, so every blueprint and test that uses them still builds (they are on a 0.5 m grid: a new part on one of
  their sockets lands half a centimetre off its lattice, which nobody sees). The blueprints move to the new parts
  separately (the drivetrain work); the old pieces can then be retired.

**The families** (the inventory's groups and sections come from each file's `"group"` and `"section"`):

| Group | Sections and parts |
| --- | --- |
| Beams | Square beams 0.25 (a cube: the frame's node), 0.5, 0.75, 1, 1.5, 2 m; I-beams 0.5 to 2 m; steel tubes (11 cm) and rods (5 cm) 0.5, 1, 2 m; angle iron and channel 0.5, 1, 2 m; braces 45 degrees across 0.5, 1, 2 m and 1:2 across 0.5x1, 1x2 m, cross braces 0.5 and 1 m; junctions (corner, T, cross, 3-way corner, 6-way hub); L and corner brackets, gussets 0.25 and 0.5 m; beam and tube end caps; Blocks (the old Frame Block and Frame Beam) |
| Panels | sheet steel and plate steel 0.25 to 2 m (squares, halves 0.5x1 and 1x2, quarters); wood planks, glass panes, mesh grates, tread plate; openings (window frames 1 m and 1x0.5 m, gun port, vent, hatch frame, door frame 1x2 m: real holes, rounds go through them); triangle plates 0.5, 1, 2 m; Body panels (the old Bumper and Fender) |
| Armour | armour plate 0.5 to 2 m, heavy armour 0.5 to 2 m, gun ports in both |
| Shapes | wedges 1:1 (0.25, 0.5, 1 m), 1:2 and 2:1; slope (outer) and inner corners 0.5 and 1 m; quarter rounds 0.25, 0.5, 1 m and curved plates (quarter pipes) 1 and 2 m; pyramid caps; round and chamfer trims 1 and 2 m |

**Materials** (mass and health grow with the area or length; the armour is the flat amount taken off every hit, the
same for bullets, the construct guns' rounds and blasts; burning ignores it):

| Material | Thickness | kg per m2 | Health (1 m2) | Armour | What it stops |
| --- | --- | --- | --- | --- | --- |
| Glass | 3 cm | 9 | 28 | 0 | nothing (any round breaks it) |
| Wood | 5 cm | 7 | 90 | 1 | nothing; it burns |
| Mesh grate | 3 cm | 5 | 80 | 3 | bars you see and shoot through |
| Sheet steel | 2 cm | 8 | 70 | 6 | the toy blaster |
| Tread plate | 3 cm | 12 | 140 | 10 | buckshot pellets up to 10 |
| Plate steel | 5 cm | 20 | 160 | 20 | pistols and SMGs up to 20, every buckshot pellet |
| Armour plate | 8 cm | 45 | 320 | 40 | every SMG, rifle, LMG and buckshot round, pistols up to 40, the mounted MG |
| Heavy armour | 12 cm | 80 | 550 | 68 | every pistol, SMG, rifle, LMG and marksman round and buckshot (snipers over 68, the slug, the autocannon, cannon, rockets and blasts get through) |

Steel beams: square 10 kg a metre, armour 8; I-beam 13 kg, armour 10 (tougher); tube 4 kg, armour 5; rod 1.6 kg, armour
3; angle 4 kg and channel 6 kg, armour 6 and 7. Shapes are solid steel (armour 8). Armour panels give more scrap than
their weight in steel (their `"scrap"`). Wood and glass are fitted with the wrench, everything else with the welder;
heavy armour takes 5 s to weld.

**Shapes**: a part box may take one of the weapon builder's contoured shapes (`"shape"`: wedge, arc, cylinder, bevel,
rounded...; `Source/Zombies/PartShapes.cs`, Parts/README.md "Shapes"), but only as a detail box (drawn only): collision
and hit areas stay boxes, so a shaped part stands on **hidden** solid boxes (`"hidden": true`: solid, never drawn) that
step along its shape (a wedge's slope in 4 to 12 steps). The shape turns with its part (`CPartBox.Model`); the tiles,
ghosts and the part in your hands draw it too.

**Pictures**: `--structure-shots` writes `smoke_structure_*.bmp`: a gallery wall per group (every part of the group on
the ground in the inventory's order, from above and close up) and a small frame welded from the parts through the
building rules. `--inventory-shots` adds the four groups' pages.

**Choices made (the developer may change them)**: the old big pieces are kept unchanged (not rescaled) so the
blueprints still build; the armour numbers (40 and 68) are set so armour plate stops every automatic small arm and
heavy armour everything short of a sniper rifle; glass is see-through ([Glass](#glass), below); a mesh grate is solid bars with real gaps (rounds pass between them); a part's inventory place is data
(`"group"`, `"section"`), the top folder's parts listed after the subfolders'.

### Glass

A part box with `"glass": true` (or how much of it is drawn, 0.1 to 0.9; `true` is 0.3) is a see-through pane. The four
Glass Panes' panes are (`tools/constructs/structure_parts.py` writes them); the "Construct glass" self-test checks every
part of `"material": "glass"` has one. A glass box may be solid or a detail, not hidden.

- **Drawing** (`Source/Render/ConstructRender.cs`): the opaque pass leaves panes out and keeps them for `DrawGlass`, which
  runs in the blended pass after everything opaque (the mode's `DrawObjectives`): farthest first by distance from the eye,
  blending on, depth writes off, lit and fogged by the box shader (mode 0, so both GL paths; no new texture units or
  bindings). A pane is tinted by its colour, drawn more opaque and lighter at grazing angles (a little reflection), and
  has a thin lighter strip along each long edge so it still reads as glass edge-on. At most 512 panes a frame are kept;
  more are drawn opaque. The inventory's tiles (`SandboxTiles.DrawTile`), the part in your hands (`ToolRender.Emit`) and
  a stored or packed pane draw it blended after the rest; a ghost's pane is fainter than its frame.
- **Collision**: a pane is a solid box like any other: players, constructs and wrecks are stopped by it.
- **Bullets**: a player's round goes through up to four panes (`Game.SandboxRay`), hits what is behind (a player in a
  cab, a part, the map) and damages each pane it passed, at full strength. A construct gun's round does the same. Blasts
  hurt glass like any part.
- **Breaking**: glass is weak (health 13 to 48, armour 0). A broken pane **shatters**: it is removed, no wreck piece falls,
  and a `CFireKind.Shatter` event (protocol 87: where, and its world bounds' half size in centimetres) bursts small shards
  out of it on every PC (the prop debris pool) with the glass sound.

**Choices made (the developer may change them)**: the opacity 0.3; rounds keep their full damage through glass; glass
shatters whole (the frame round a Glass Pane goes with it); sorting is by each pane's centre (two panes crossing each
other may blend in the wrong order where they cross).

## New constructs (free placement)

Board item dev-sandbox-new-construct-nc01 (the developer's note, 2026-10-29): a construct no longer only comes from a
blueprint or the map. Holding any placeable part with a tool out, while not aiming at a construct's socket, shows a ghost
of the part on the **surface you aim at** (terrain, the map's boxes, a floor or deck of the map); holding fire with the
right tool for the part (decision 2: welder for frames, panels and foundations, wrench for bolted parts) sets it down
there as a **new construct rooted at that part**, with its own sockets to build on (`Source/Constructs/ConstructPlace.cs`;
milestone 1's "set it down on the ground", which took only a yaw, is what it generalises: the same `CActKind.PlaceNew`
and `EditKind.PlaceNew`).

- **The pose**: the construct's up follows the surface's normal, except on near-level ground (within 12 degrees), where
  it stands upright and rests on the highest ground under its footprint's corners (a bump under one corner lifts it, so
  it never starts inside the terrain). Its yaw is the look's. The **arrow keys** turn the part through its 24 turns
  (`Rot24`) relative to the camera and **R** puts it back upright ([Turning the ghost](#turning-the-ghost)). On a wall it
  stands out of the wall.
- **The grid** (the inventory window's "Snap new constructs to the grid", off by default): the spot snaps to a 0.5 m world
  grid across the surface and the yaw to 90 degrees, so constructs set down apart line up (a block set down next to
  another one meets it face to face).
- **Green or red**: the ghost is green when it fits and red, with the reason in the look text, when not: inside the ground
  or a wall, someone standing there, another construct in the way (every part box of every construct near it, turned
  boxes tested exactly), no surface, too far, or the limit reached. The server checks again when the placement starts and
  when it is done (something may have moved there in the 1.5-2.5 s it takes).
- **Free or anchored**: a placement is **free** (physics: it rests on what it was set on and falls if that goes) unless the
  part is a **foundation** (anchored as before) or creative's **"Anchor new constructs to the ground"** toggle (the
  inventory window; only on maps with infinite parts: elsewhere build on a foundation) **pins** it: `Construct.Pinned`, held
  still like a foundation, sent in CBody's flags.
- **The Construct Core** (`Constructs/Parts/ConstructCore.json`, `"core": true`): a 0.5 m orange welded block with a socket
  on each face, in the inventory's Structure group: the obvious starting point. A core is never a loose part (it can't be
  picked up with F; a single dropped Frame Block can) and is the root after seats and foundations.
- **Building onto a wreck or a loose part** (a dropped part, the map's loose parts) counts as building on that construct:
  the ghost sockets show on it, and the first part fitted **claims** it (no longer a wreck, the builder's own, so it no
  longer clears away as debris).
- **Joining two constructs**: with the welder out, aiming at a part whose free structural socket meets a free structural
  socket of another construct (face to face, within 3 cm, both on their main bodies rather than beyond a joint, the two
  bodies square to each other) shows "touches construct N - F to weld the two into one"; **F** joins them
  (`CActKind.Join`, `ConstructWorld.Merge` in `ConstructJoin.cs`). The other construct's parts move into this one's frame
  (each takes one more Rot24 turn, so nothing bends), with its welds, joints (their pivots and axes turned), wires, fuel
  and charge; every other socket pair the two meet at welds too; a coupling between them goes; a wire between them becomes
  one of its own. Both must be yours, or free for anyone to take (a wreck, a loose part, the map's): someone else's
  working construct can't be joined. The one aimed at stays (unless only the other is yours) and becomes yours. Refused
  while anyone sits in either, or past the caps (96 parts, 8 joints, 64 wires a construct).
- **Ownership and limits**: what a player sets down (or claims, or joins) is theirs (`Construct.Owner`; factions later).
  A player may own **12** constructs (wrecks and loose parts don't count); past that a new one is refused ("take one apart
  or join two"). The world's caps (64 bodies, 1,200 parts) still hold.
- **Network** (protocol 84): `CAct.PlaceNew` carries the surface point, its normal, the yaw, the turn and two flags (grid,
  anchor); the server finds the surface again under the point itself. `CAct.Join` names the part. CBody's flags carry
  "pinned". Everything else (the new construct, the merged one, the one that went) travels as before.
- **Cost**: the client runs the pose, the fit check and the join search every frame while a part is held: fixed loops over
  the bodies near it, no allocation (the self-test measures it).

Pictures: `--placement-shots` (Program.PlacementShots.cs, under xvfb like the other shots) writes
`smoke_placement_*.bmp`: the core's ghost on open ground with the HUD, a beam's ghost turned on its side, a red ghost over
a construct with the reason, the core built on, the join offer, and the joined construct pushed as one.

**Self-test**: the **Construct placement** section (`Source/App/SelfTestConstructPlace.cs`): the Rot24 helpers (compose,
quaternions, nearest) and the rotate key's order; the pose on level ground, on the grid and on a wall; a beam turned on its
side joined to a block keeps its place in the world; in an offline game on the yard the core set down on open ground is a
one-part construct of the player's with six sockets to build on, a Frame Block attaches to it; placing over a construct,
over a player, in the air, or anchored outside creative is refused with the reason; a free block rests on the ground and
moves when pushed while a pinned block and a foundation don't; building onto the map's loose Frame Beam claims it; a block
set down beside the first construct is found to touch it and joined (every part reaching the root, the other gone); at 12
constructs a new one is refused; the placement checks allocate nothing; and over a real loopback a client's pinned core
is pinned and theirs on its copy, and the block it then joins in is gone there too.

**Choices made (the developer may change them)**: the level snap is 12 degrees; the grid is 0.5 m and off by default;
the limit is 12 constructs a player; joining is instant (the weld, no timer) and on F with the welder; anchoring without a
foundation is creative-only; the core is a frame-role part with a flag rather than a new role; placing on top of another
construct's surface without a socket isn't offered (only map surfaces).

## Drivetrain parts (the families)

Board item dev-sandbox-drivetrain-parts-dp01 (the developer's note, 2026-10-29). Code: `Source/Constructs/ConstructDrive.cs`
(the drive line, the engine each tick), `ConstructDriveParts.cs` (the blocks), the wheels in `ConstructPhysics.cs`. Data:
`Constructs/Parts/Drivetrain` (53 files) and the rescaled Wheel, Suspension, Engine, Transmission, Differential and Steering
Pivot. The scale is the developer's "Realistic" chart ([Structural parts](#structural-parts-the-scale-chart)).

### The drive line

The engine must touch a working **transmission**; from it the drive runs through working **differentials** and
**transfer parts** linked to it, each only through its **ports** (a shaft in one end and out of the other, never its side),
to the **suspensions** bolted to a differential's or a **hub**'s ends, or on the leaf of a **steering pivot** that a **U
joint** or **CV joint** feeds. Each driven wheel keeps its reduction (the gearbox's gear times the chain, belt and gearbox
ratios on its way, times its differential's or hub's final drive), what is left after each part's losses, the most
torque its line carries (a belt slips above it) and its **gates**: a clutch, a transfer case's front output, a U or CV
joint's angle. The torque is shared between the drive groups that touch the ground and inside each group by its kind:
**open** (a wheel in the air takes it all: 10% reaches the other, and no wheel is driven harder than the one that grips
least), **limited slip** (60% to the wheel that grips, half as hard again), **locked** (all of it; a locked axle with both
wheels down scrubs the turn: it damps the yaw), **lockable** (open or locked by the linking tool's R or a wire).

**Why it won't go** (the HUD's red line): "the electric motor needs a working battery", "a steered wheel needs a U joint or
CV joint to be driven", "a stub axle carries no drive: fit a hub or a differential", "the drive shaft 1 m is fitted by its
side: the drive goes in and out of its ends", "the drive shaft 1 m is wrecked", "a caster wheel can't be driven", "a
piston can't carry the drive", besides milestone 2's. A **drive note** (orange, sent to clients) says why a built line
isn't driving right now: the clutch is out, the U joint is bent past its angle, the belt is slipping, the battery is flat,
a diesel needs a battery for its starter; "shifting" and "spooling up" in grey.

### The engine each tick

- **rpm**: from the driven wheels touching the ground (speed over radius times the reductions), never below idle; pulling
  away the clutch slips and the engine revs towards its peak; in the air it revs freely; it can't go much past the
  limiter. **Torque**: the peak times the curve at that rpm (petrol rises to its peak; diesel strong low down and falls
  off early; electric flat to its peak then constant power; turbine low at idle; flat), times the throttle (a turbine
  times its **spool**, which takes its `spool` seconds), and nothing at the limiter or during a **shift** (its `shift`
  seconds). Fuel use grows with the throttle and the rpm (a turbine burns at idle too); an electric motor draws the
  batteries (its power over 0.9); a hamster wheel needs nothing.
- **Gears**: manual and sequential boxes take the driver's **R** (up) and **sprint** (down) when nothing is wired in, the
  linking tool's R or a wired signal as before; an **automatic** goes up at 90% of the limiter and down when the lower gear
  would stay under 70% (a wired signal caps its highest gear); a **CVT** holds the engine at its peak within its range; a
  **reversing** box backs up at its one ratio (others back up in their reverse ratio, 1.1 times first). A gear picked
  standing still costs no shift time.
- **Starting**: a diesel or a turbine (`"starter": true`) needs a battery: without one, "It won't start". An electric
  motor runs while a working battery's links reach it with charge. An engine with nothing to drive still runs (a power
  take-off).
- **Wheels**: grip on terrain (dirt) is 85% of hard ground times the tyre's `offroad`; `side` scales the sideways grip
  (an omni wheel slides, a track unit turns on the spot); `roll` is a share of the load; a wrecked tyre with `flat` stays on
  at three quarters of its radius with half its grip. A **track** unit is one part with several contact rays along its
  length (6 on the 5 m Track); a construct whose driven wheels don't steer **skid-steers** (the steering drives one side
  forward and the other back). Air drag (0.006 v squared) tops out the tall gears.
- **Suspension**: a spring carries at most its `maxLoad` (kg a wheel): more and it sags and bottoms. An **adjustable**
  strut's presets (R) set its stiffness, damping and, with `hold`, a ride height; an **active** (hydraulic) strut holds the
  height its wire asks (0 low, 1 high, unwired half way, 0.36 m between) and draws 150 W on an electrical construct.
- **PTO**: a turntable, motor hinge or piston bolted to a power take-off on the drive line runs from the engine, only while
  it runs, with no battery.
- The configure tool is still to come: the **HUD** shows the speed, rpm, torque, gear and fuel while driving (and "R gear
  up, Shift gear down" for a manual), and the **look line** an engine's rpm, torque and curve, a gearbox's ratios and kind,
  a differential's kind and ratio, a transfer part's ratio or state (2WD/4WD, engaged), a wheel's size and grip (FLAT), a
  suspension's travel and preset or height.

### The families

| Family | Parts (no single best: mass, grip, power, fuel and toughness trade off) |
| --- | --- |
| Wheels | Wheel (0.8 m road), Small (0.6 m), Large (1.1 m), Off-Road (grips dirt, worse on roads), Monster (2.4 m, 220 kg), Solid (no puncture, armour 25, less grip), Slick (most grip on hard ground, slides on dirt), Spiked (bites dirt, skates on roads), Caster (0.4 m, swivels, can't be driven), Omni (slides sideways), Tank Road Wheel (steel), Track (5 m) and Short Track (3 m), sprockets at the back |
| Suspensions | Suspension (0.3 m, 600 kg), Stiff (0.12 m), Long-Travel (0.6 m, soft, light loads), Leaf Spring (3 t a wheel, tough), Adjustable (soft, medium, stiff, high, low), Rigid Mount, Hydraulic (active) |
| Engines | Engine (0.5 m petrol, 260 N m), Small Engine (0.4 m single, 70 N m, 9000 rpm), V8 (1 m, 520 N m, thirsty), Diesel (1 m, 820 N m at 1800, 3200 rpm, starter), Electric Motor (380 N m from standstill, battery), Turbine (700 N m to 12000 rpm, spools 2.5 s, thirsty, starter), Hamster Wheel |
| Transmissions | Transmission (3-speed manual), 5-Speed, Automatic (4, 87% through), CVT (84%), Reversing, Low-Range (12:1 and 8:1), Sequential (6, 0.12 s shifts) |
| Differentials | Differential (open, 1.1 m), Limited-Slip, Locking, Wide Differential (open, 1.6 m), Live Axle (locked, 1.6 m) |
| Transfer | Drive Shaft 0.5, 1 and 2 m; U Joint (15 degrees), CV Joint (45); Right-Angle, T and Reduction (2:1) gearboxes; Transfer Case; Chain Drive 1:1 and 2:1, Belt Drive (0.67:1, slips over 900 N m); Clutch; Power Take-Off; Wheel Hub (4.1:1); Stub Axle |
| Tanks, power | Small (15 l) and Large (150 l) Fuel Tank, Battery Pack (20 MJ) |

### The rescaled blueprints and the examples

The **Buggy** (and the Wired and Hood Gun Buggies) is 3.9 by 1.8 m on two 2.5 m square-beam rails, a cross member, a
deck plate and a wedge nose, 0.8 m wheels 2.9 m apart; the engine sits on the gearbox over the differential, its tank
behind on a fuel line; the steering pivots are 18 cm knuckles. The **Trailer** is a square-beam drawbar with a 1 m bed.
New examples: the **4x4 Truck** (8.2 m: a diesel, an automatic, a transfer case in 4WD, right-angle gearboxes onto a locked
live axle and an open wide axle with CV joints into its steering pivots, leaf springs, large wheels), the **Tracked
Crawler** (7.1 m: a V8 and an automatic on a wide axle driving two 5 m tracks, skid steering), the **Racer** (a V8, a
sequential box, a limited-slip differential, slicks on stiff struts) and the **Electric Cart** (2.6 m: an electric motor
under a battery pack, a reversing box, small wheels). The truck's and crawler's ladders are still the old 2 m Frame Beams
(their drive lines sit at a 0.5 m beam's height); everything else is on the structural parts.

### Self-test and pictures

The **Construct drivetrain** section (`Source/App/SelfTestConstructDrive.cs`): every part file loads and 15 bad drivetrain
blocks are refused for the right reason; the buggy, truck and crawler match the chart; each wheel, suspension, engine and
transmission changes the drive measurably (distance on hard ground and dirt, the turn, ride height and roll, speed off the
line and the top speed, climbing a 15 degree ramp with a small engine; slicks faster on roads and slower on dirt, off-road
tyres faster on dirt, a turbine slow off the line then faster, a hydraulic strut's height on a switch, the adjustable
presets, an automatic shifting, a CVT at its peak, a reversing box as fast back as forward, a sequential box's shorter
shift, low range climbing furthest, a diesel without a battery refused); a rear wheel in the air gets 5%, 60% or 100% of the
drive through an open, limited-slip or locked differential, and the locked one turns less; the drive reaches a wheel
through every transfer part kind with the right ratio, and is refused with its reason for a steering pivot without a U
or CV joint, a stub axle, a shaft fitted by its side and a wrecked shaft; a clutch on a switch, the truck in 2WD and 4WD,
its U joints losing the front drive at full lock (CV joints keeping it), a turntable on a PTO turning only with the engine;
the five examples drive and turn (the crawler on the spot) and the racer is the fastest; a client gets the rpm and the
note; eight vehicles driving at once allocate nothing (about 0.2 ms a tick in the container). `--drive-shots`
(Program.DriveShots.cs) writes `smoke_drive_*.bmp`: the row of examples, each with a player beside it, the truck's front
axle and transfer case, the crawler's track, part galleries and the driving HUD.

### Choices made (the developer may change them)

1. Gear ratios are **reductions** now (the old Transmission's 0.6/1.0/1.4 speed multipliers became 4.4/3.0/2.1), and a
   differential has a final drive (4.1 on the buggy's).
2. A **steered driven wheel needs a U or CV joint** between its axle and the steering pivot (milestone 2 let a pivot on
   a differential drive); a U joint loses the drive past 15 degrees of lock.
3. **Dirt** (terrain) grips 85% of hard ground; the old rolling resistance (a share of the speed every tick) became a share
   of the load plus air drag, so tall gears and big engines matter; the open differential's "no harder than the least
   grip" keeps a car straight at the grip limit.
4. A wrecked **tyre goes flat** and stays on (the shipped wheels); shoot the suspension to drop a wheel.
5. **Shifting by keys**: R and sprint for an unwired manual or sequential box (sprint isn't used while driving).
6. The truck and crawler keep the old 2 m Frame Beams for their ladders (the structural parts' square beams are 0.25 m,
   below the drive lines' 0.5 m height); a later pass can move them.
7. 247 of the 250 part ids are used (structure 110, drivetrain 53, storage); more part families need 2-byte ids (done
   with the routed links: part ids are two bytes since protocol 90, up to 4,096 types).

## Routed links (decision 20)

Board item dev-sandbox-routed-links-rl02 (the developer's design, 2026-10-30). Code: `Source/Constructs/ConstructRoutes.cs`
(the links, their rules, flow, damage, the wire format), `ConstructRoutesBuild.cs` (blueprints' `"links"`, the migration),
`ConstructEditsRoutes.cs` (the server's edits), `Source/App/Program.SandboxLinks.cs` (the linking tool on this PC),
`Source/UI/SandboxLinkWheel.cs` (the link wheel), `Source/Render/RouteRender.cs` (drawing them).

One routed-link system on the linking tool replaces the invisible wires and the Fuel Line part. A link is a polyline of
**nodes** from an **output** to an **input** on one construct, routed node by node over its surface, drawn by one
segment piece and one elbow piece per type (instanced), and simulated as a graph edge (never a physics body).

| Type | Look | Routing rule | Carries | Segment health |
| --- | --- | --- | --- | --- |
| Data | small flat cable | free | on/off and number signals (the logic's wires) | 20 |
| Power | larger flat cable, red stripe | free | electricity from a battery or alternator to anything that draws it | 40 |
| Pipe | round with rings, one style per fluid | bends in 15 degree steps | fluids from tanks to engines | 80 |
| Drive shaft | round steel, a U joint at each bend | straight, at most 45 degrees a bend (and into a port), no corners (a gearbox does that), no bare junctions (a T gearbox does that), never across a joint (a U or CV joint device does that) | torque from engines, gearboxes, transfer parts and differentials, port to port | 250 |
| Chute | square duct with rims | bends in 45 degree steps | items between storage parts (a load of bulk or one stored part a second) | 80 |

**Pipe styles** (the developer's hybrid choice): a pipe started at a tank takes the style of what the tank holds
(`"fuelTank": {"fluid": ...}`, fuel by default); the link wheel's pipe ring overrides it. Fuel: thin with hazard bands;
water: blue with flanges; oil: dark with clamps; steam: thick lagging. Each style has ratings (pressure, heat, rated for
flammable fluids, residue), each fluid its needs, and a **wrong pipe is allowed** with readable consequences: fuel in a
water pipe weeps (0.05 l/s) and catches fire after 20 s; something too hot or at too much pressure for its pipe bursts it
after 30 s; fuel through an oil pipe makes the engine sputter (60% torque). The look-at line says what is wrong and a
warning marker sits on the pipe.

**Routing** (the linking tool): Fire on an output (a picker when it has several ports: a shaft's drive sockets); each
Fire places a node on the surface under the crosshair: off the surface by the link's radius, on the construct's 5 cm grid
along the surface, onto the straight line on from the last segment when close to it, lined up with a neighbouring link's
nodes, then turned to the type's angle rule. The preview shows the link so far and the next segment, red with the reason
when a rule is broken (too close, a node sinking into a part, a segment through a solid part, the bend, the reach: at
most 8 m a segment, 24 m a link, 32 nodes). Fire on an input finishes it (a picker for its port); Aim or Backspace takes
the last node off; Esc cancels. A straight data cable may still go into **another** construct's input (milestone 3's wire
between constructs). The server checks the whole link again (`ConstructRoutes.Check`) from where the player stands.

**Junctions**: Fire on a link of the type picked starts a branch at that point (a junction node goes into the link).
Power, fluids and items split evenly at a junction (each way gets an equal share of what arrives: the look-at line shows
the share; a chute's branches take turns); data copies the signal to every branch. Shafts don't branch.

**Attachment**: every node sits in the rest frame of the part it is on, so links on jointed parts move with them; a cable
or pipe crossing a joint flexes there. Links go with their parts when a construct splits or is joined, and with the
blueprint.

**Destruction and repair**: bullets, construct guns and blasts hit link segments like parts (each on its own). A cut
segment stops the flow beyond it: a cut fuel pipe pours fuel out (0.5 l/s) and a blast through it lights the tank, a cut
power cable sparks. The wrench or the welder (Fire, nothing in hand) mends a link; the edit ring's re-route starts it
again from its output.

**Edit modes** (the link wheel's Edit ring): delete a link (and its branches), move a node, re-route a link, highlight
all (every link bright through the parts). Data and power cables are dimmer unless the linking tool is out.

**Migration**: logic wires are data links routed straight (a blueprint's `"wires"` still work); when a blueprint is built,
each chain of Fuel Line parts between a tank and an engine becomes a fuel pipe along the same path, and each chain of
plain drive shafts between two drive parts a shaft link from port to port (the shipped Buggy, Wired Buggy and Hood Gun
Buggy now carry their tank on the deck with a fuel pipe; the 4x4 Truck's five shafts became two shaft links). A chain
whose removal would drop a part (a tank hung only on its line) keeps the old parts, which still work. The Fuel Line and
the plain drive shafts are `"legacy": true`: not in the parts menu or on a rack. U and CV joints, gearboxes, T boxes,
transfer cases, clutches, chain and belt drives (they have ratios), power take-offs, hubs and differentials stay devices.

**Blueprints**: `"links": [{"type": "pipe", "style": "fuel", "from": step, "to": step, "nodes": [[x, y, z], ...]}]`, with
`"fromPort"` / `"toPort"` (socket names) for shafts and `"branch": [link, segment], "at": [x, y, z]` in place of `"from"`
for a branch; nodes in the construct's frame, each on the part nearest it.

### Self-test

The **Construct routed links** section (`Source/App/SelfTestConstructRoutes.cs`): each type's bend rule and the snap
keeping it, nodes snapped off a surface never inside it, a sunk node, a segment through a part and wrong ends refused;
a switch lights two lamps through a data junction, a cut branch and its repair; a power cable carrying the circuit past a
wrecked tank, its junction splitting evenly, cut and sparking; fuel reaching two engines through a pipe junction, a cut
branch starving one and leaking; a water pipe catching fire, an oil pipe sputtering, the ratings; the buggy's fuel pipe
cut and repaired; the migrated truck's shafts driving four wheels (two with the front shaft cut), a bare shaft junction
and a shaft corner refused; a chute junction sharing scrap evenly; links following a driving buggy and a turning turret;
blueprint `"links"` and `CBody` round trips (a short packet refused); the server's linking tool checks; the migrations;
300 part files with their own ids; no allocation in 120 ticks of three constructs with links, nor in rays at a cable.

### Choices made (the developer may change them)

1. Tunable constants in `ConstructRoutes`: pipe 15 degree steps, chute 45 degree steps (developer), shaft at most 45
   degrees a bend (developer), shaft junctions need a T gearbox (developer); 5 cm node grid; 8 m a segment, 24 m a link,
   32 nodes; segment health 20/40/80/250/80; a cut fuel pipe leaks 0.5 l/s; a weeping pipe lights after 20 s, a weak one
   bursts after 30 s, residue gives 60% torque; a chute moves a load (10 bulk) or one part a second.
2. Opening the link wheel: the tool wheel key while the linking tool is out (its Tools segment, rested on, opens the tool
   wheel); rings open by resting the pointer on Pipe, Edit or Tools for 0.3 s.
3. Power cables add to the frame circuit (the frame still carries power, so every earlier construct works); a cable
   carries the circuit both ways.
4. A shaft link is a drive-line edge with no ratio (each U joint keeps 99%); chain and belt drives stay devices.
5. A link with no nodes is checked like any other (it may not pass through a part); migrated wires and blueprint links
   with no nodes are straight and checked for reach only.
6. Repairs are instant with the wrench or the welder; junction ratios are even (the configure tool can make them
   settable later).

## Performance and caps

At most 64 bodies, 96 parts a construct, 1,200 parts in all, 8 joints and 12 wheels a construct (128 joints in all),
64 routed links and 768 link nodes a construct (512 links in all), 4 inputs a part, wires up to 8 m (12 m between constructs, 64 of those), 32 couplings, 40 hull points a body, 64 points and 48 swept edges beyond joints a construct, 1,024 contacts a tick. Fixed arrays throughout: the self-test drives a buggy round the yard for 120 ticks and
measures **no allocation** (with the turret turning, the lift and the motorised gate moving and their logic running);
the yard's constructs take about 0.1-0.4 ms a tick in the container. The logic step is a few spans on the stack. Sleeping bodies cost
nothing. Other modes never touch any of it (every hook returns at once outside Sandbox; the seeded mode digests are
unchanged).

## Self-tests

The **Structural parts** section (`Source/App/SelfTestStructureParts.cs`): every file in `Constructs/Parts/Structure`
loads (none refused anywhere) and keeps the cell rules; each one goes onto a 0.25 m cube by every socket on all six
faces in all four spins (every part reaches all 24 turns; about 53,000 placements), its origin always on a whole
centimetre and its cells on the cube's lattice; every socket takes a copy of the same part, which welds on; each shaped
box turns exactly with its part in all 24 turns; armour against the real guns' damage (armour plate stops every SMG,
rifle, LMG and buckshot round and pistols up to 40, lets marksman and sniper rounds through; heavy armour stops every
small arm up to 68 and lets bigger sniper rounds through; plate steel stops buckshot, not rifles; sheet steel and glass
stop nothing; the mounted MG is stopped by armour plate, the autocannon, cannon and rockets get through heavy armour);
the materials ladder (glass to heavy armour) stops more at each step; a round down through the Gun Port Panel's slit goes
through while one beside it hits the plate; the fitting checks and blocked hits allocate nothing; and the inventory lists
every structural part under its group and section, with the old blocks after the new beam sections.

The **Construct weapons** section (`Source/App/SelfTestConstructWeapons.cs`, milestone 3): the weapon part files load
(eight guns, the five heavy ones among them, the gun parts, the boxes) and ten kinds of bad file are refused for the
right reason; a rifle gun built part by part with the tools (the welder refused on a gun part; its problem goes from "no
barrel" to "no feed" to none as the barrel and magazine go on; 600 rpm, a 30-round magazine, AP penetrating, the scope
tightening it; the parts weigh what their files say; a switch wired in by the linking tool fires 10 shots a second from a
magazine loaded out of the box); the Hood Gun Buggy's gun points with the buggy as it turns and its driver's fire key
fires it (and stops when let go); the Gunner Turret follows its gunner's look three ways round and its fire key fires it,
the lever turning it again when the seat is empty; the Sentry draws no fire (and doesn't turn) for a player beyond its
sensor's range, turns onto and fires at one in range, and stops when they go; an ammo box feeds its gun through a feed
chute, 25 rounds fire exactly 25 shots, and a shot-off chute leaves the gun with no box; a gun's rounds hole an ammo box
which cooks off a beat later and hurts a block a metre away; ball rounds stop at a wood panel, AP rounds go through it into
the block behind (and only that), HE rounds splash a block off the line, incendiary rounds set the wood alight but not the
steel and it burns down on its own; in an offline game the local player in the yard's Gunner Turret turns it with their
look (their view held) and shoots a player 9 m out (credited), and the yard's sentry shoots a player who walks up but not
one beyond its range; no allocation in 120 ticks of a sentry and a gunner's turret firing (aiming, loading, the fire and
ammo messages written); and a real host and client: the client draws every round the server's gun bench fires from its
own copy of the gun, its ammo matches, and a late joiner gets the rounds.

The **Construct merged receivers** section (`Source/App/SelfTestConstructMerge.cs`): the rules file loads and six kinds of
bad file are refused; the diagram's shapes (T, L and 1x4 stay single; 2x2, 3x3 and 4x4 merge; a 3x4 is a 3x3 and three
singles; two 2x2s side by side; turned and heavy squares merge, mixed types and a turned member don't); finding a 4x4's
merges 200 times allocates nothing; built with the tools, the fourth receiver of a 2x2 pops the barrel, magazine and box
off the first one's own sockets (the barrel into the hands, the rest into the backpack), the members' own sockets refuse gun parts,
the centre sockets refuse an ammo box and show ghosts; a barrel and magazine on the centre set are 2x (twice as long, four
times the weight; the ammo box as it is), the gun 4x the damage, half the fire rate, 4x the magazine and recoil, firing 4
rounds a shot out of its load and box; a client's copy and a copy rebuilt from the parts find the same merge, scales and
gun; five more receivers make a 3x3 with the barrel and magazine moved on at 3x (9x the damage, 9 a shot); the wrench on
the bottom-right corner takes only that receiver (a 2x2 and four singles left, the centre parts back at 2x); then the
bottom row's second: the 2x2 above takes over and the barrel follows onto its centre front; then the middle: no square
left, the centre parts pop off; a 2x2 has its members' health together, and a cell shot to pieces breaks it and drops its
centre barrel as a loose part.

The **Construct joints** section (`Source/App/SelfTestConstructJoints.cs`): a flap on a passive hinge with its axis flat,
latched open and flung down at 6 rad/s onto the yard's terrain, stops on it (no part of it below the ground at any tick),
then sleeps and stays put; a free arm (a beam, a motor hinge, a beam) driven into the floor lifts its own construct more
than 0.3 m and never goes into the floor; the Motor Gate's door opening into a post stalls at it, stays stalled and
awake, and goes on to its limit when the post is taken away; the Turret's arm turned into another (anchored) construct
stops at it; the Gate's door swung at 10 and 25 rad/s at a 5 cm wall never gets through it; the yard's six jointed
constructs all sleep after 3 s alone; no allocation (a stalled joint, and a dozen jointed constructs moving at once) and
the dozen inside 2 ms a tick. Most of it on a flat test floor with box walls (`JointTestWorld`). Pictures:
`--joint-shots` (Program.JointShots.cs) writes smoke_joints_*.bmp of the flap, the arm and the turret under xvfb.

The **Constructs** section (`Source/App/SelfTestConstructs.cs`): part files load and 12 kinds of bad file are refused
for the right reason; the buggy settles level on four wheels, drives 30+ m in 3 s, turns right, brakes, and climbs the
ramp onto the deck; a shot-off fuel line stops it; a panel's armour shrugs off a small hit; a broken suspension drops
its wheel as a wreck; a blast damages parts; the gate's door opens, shuts and swings when walked into while the anchored
gate stays put; a car built part by part through the edit rules (bare hands refused, the wrong tool refused, timed
fits, ghost sockets offered, a wheel taken off into the hand, sit and stand); a client copy from the wire matches and a
truncated packet is refused; no allocation per tick; and a real host and client: mirror, sit and drive with the client's
own keys, stand, a bare-hands build refused with its reason, a broken part mirrored, a late joiner gets everything.
"Mode modules" plays a short bot match in Sandbox on Construct Yard.

The **Construct logic** section (`Source/App/SelfTestConstructLogic.cs`, milestone 2): bad joint, logic and blueprint
files refused for the right reason; the Lift's piston reaches and holds its 2 m with two riders, sinks when overloaded
and comes down on its button; the Motor Gate's door opens and shuts on its button, can't be pushed or opened by hand;
the Turret turns both ways on its lever and holds in the middle; the buggy's steering pivots follow the driver inside
their limits; a locked hinge stays shut; a bench of logic parts on a foundation (AND, OR, NOT, a chain of three NOTs that
takes exactly three ticks, a NOT wired into itself flipping every tick, a 1 s timer, a 0.5 s delay, a button through a
toggle into a lamp, a lever through scale and compare, an input reader on a seat's fire key, a proximity sensor); the
linking tool's rules (bare hands and the wrong tool refused, eleven refusals with their reasons, cutting wires, cycling
a setting); a shot transmission and a shot differential stop the buggy; a switch wired into the engine drives it with
nobody in the seat and a brake part stops it; the wired buggy's horn and lamps follow the driver's keys; and in a real
game the lift raises the player standing on it, a passenger rides along in the second seat, and a player in the
buggy's way is hurt. "Constructs"' loopback now also checks the client and a late joiner mirror the joints, wires and
signals.

The **Construct salvage** section (`Source/App/SelfTestConstructSalvage.cs`, milestone 3): the scrap values and the
map's `infiniteParts`, `debrisSeconds` and `debrisNear` (bad values refused); in an offline Sandbox game on the yard a
wheel that fell off the buggy as a wreck is salvaged (the welder refused, the wrench timed) into the inventory with the
hand left empty, the wrecked suspension gives its scrap, someone else's working gate is refused until a part of it is
wrecked and then salvaged; with infinite parts off the parts menu refuses a part the inventory lacks, hands one out of
it, a fit uses it and the next comes into the hand, the player's own gate's door goes back into the inventory, a held
counted part goes back on respawn, and the HUD shows the counts; with them on the player's own parts come off into the
hand; a wreck left alone goes after the map's time, one being salvaged or with a player near stays; the Sandbox tick,
the debris check and the inventory allocate nothing; and a real host with two clients: the salvaging client sees its
count (the other doesn't), a client's gate is owned by that client on every copy, and an expired wreck goes on the
clients too. `--sandbox-shots` adds `smoke_sandbox_salvage_*.bmp` (half-way through salvaging a wheel, done, a counted
part in hand) and `smoke_sandbox_debris_cleared.bmp`.

The **Sandbox parts menu** section (`Source/App/SelfTestSandboxPartsMenu.cs`): the map's `partsMenu` flag reads (on by
default, a bad value refused); in an offline Sandbox game the menu's request hands the chosen part by id to the player
anywhere, an unknown id is refused (the hands kept), the rack still refuses from afar and hands out its next kind at the
rack, a Turret spawns in front of the player and a bad blueprint index is refused, and the map flag turns both off; the
window draws headless (every part listed, a search narrows it, a pick puts the part in hand with its tool out and closes
the window) with no ImGui error and no allocation per frame while open; a real client's part by id and blueprint go over
the wire; and outside Sandbox a blueprint spawn is refused. The inventory's tabs and tree headless: every tree node (each
group, each section) lists exactly its own parts, a search narrows within a node ("lamp" in Logic finds the Lamp, in
Structure nothing), the Blueprints tab lists every blueprint and taking the highlighted one ("buggy") builds it, the Tools
tab lists every tool model and taking the first one puts it in hand; steady open frames allocate nothing with "inf" badges
and with counts from a source.

The **Sandbox inventory** section (`Source/App/SelfTestSandboxInventory.cs`), headless: the catalogue holds every part,
blueprint and tool exactly once, today's parts all have a named group (none in Other) and the five main groups exist, an
unknown role goes to Other; every tile slot gets boxes (a blueprint built as the server builds it) and a camera that keeps
every box corner inside its square while filling at least 75% of it; and every slot has its own place in its atlas page.

`--inventory-shots` draws the atlas as the game does (the log says how many frames it took and the slowest one) and
writes pictures of the inventory over the yard: the Parts tab, one group, one section, a search, the largest tiles, and
the Blueprints and Tools tabs (`smoke_inventory_*.bmp`); it runs under xvfb like `--light-shots`.

The **Sandbox tool wheel** section (`Source/App/SelfTestSandboxToolWheel.cs`): the direction-to-segment maths (every
segment's middle and both edges for 8, 4, 3, 6 and 1 segments, the wrap-around at the top, the dead zone, a pointer that
isn't a number), the pointer held at its reach so one flick turns it; the key (an unshared middle mouse button; binds
version 9 files get it, one that already used the button keeps it); in an offline Sandbox game a scripted
hold-flick-release takes out the welder (east), wrench (north), linking tool (west) and hands (south), the current tool is
marked when it opens, letting go in the dead zone cancels, the scanner says "coming later" and changes nothing, the
north-east takes out the physics gun, a menu and
death close it without choosing (and the still-held key doesn't reopen it), and it doesn't open in a seat; 240 headless
frames with the pointer going round draw without an ImGui error and allocate nothing; and a real client's pick reaches the
server.

The **Sandbox physics gun** section (`Source/App/SelfTestSandboxPhysGun.cs`): the model, the wheel slot and the tool
byte; in a bare construct world on the yard a grabbed block is pulled onto the beam's point (2 m up) and held in the
air, the wheel's distance moves it 3 m out, a snapped turn is reached, the snap maths (steps of 45 degrees, a snapped
turn stays put); a frozen block doesn't move in 2 s against gravity; with a second player's block frozen too, player
0's unfreeze-all frees only theirs (which then falls); a second player can't take a held block; grabbing a frozen one
unfreezes it; a construct with someone in it, a pinned one and one too heavy for survival are refused; the frozen state
round-trips through the structure; survival's contested rule refuses the physics gun, the welder and the linking tool
("Owned by Rival - defended") and allows them in creative, with the owner away, on nobody's and on your own; nothing
allocates over 30 ticks (unused, and holding); and over a real loopback a client grabs, steers and freezes a loose part
on the server, the host can't take it, the host's own beam (its held construct and grab point) reaches the client, and
the client's unfreeze-all frees it.

The **Sandbox tool models** section (`Source/App/SelfTestSandboxToolModels.cs`): every tool file loads as one piece with
its hands in reach, every tool in the game and every tool still to come has a model, and 13 kinds of bad file are refused
for the right reason (two models for one tool among them); in first person each model draws its boxes (plus sparks and a
beam in use) with both hands on their grips, low on the right of the view, and the equip drops the old tool before the
new one comes up and hands back to the gun after the drop; in an offline Sandbox game a bot's welder (welding: its
sparks), a carried wheel and the local player's wrench are drawn in third person instead of the gun, the scene graph
matching the immediate code draw for draw, and a gun again outside Sandbox; 240 frames of tools in use allocate nothing;
and a real host and client each draw the other's tool in their hands.

`--sandbox-shots` writes pictures of the yard, a buggy, the ghost sockets, a driven buggy, its front wheels steering,
the gate open, the motorised gate open, the lift up, the turret turned, the wired buggy with its lamps on and its
wires (the linking tool's view), a broken buggy, the inventory (open, searched, and a buggy spawned from it), and the tool wheel held open (on the linking tool, then on the greyed hoist) (`smoke_sandbox_*.bmp`); it runs under xvfb like `--light-shots`.

## Decisions made for milestone 1 (open questions it touches; the developer may change them)

1. **Joints**: only the passive hinge ships, as a reduced-coordinate angle on its body; the joint record is ready for a
   motor (turntable) and a slide (piston), which milestone 2 adds with the linking tool and logic parts.
2. **What players start with**: a gun loadout as usual, both tools always available (keys 3 and 4), and an endless
   parts rack on the map. No resources, crafting or unlocking yet.
3. **Destroyed parts** break off as wrecked debris (scrap to salvage later); engines, tanks and seats are disabled
   instead. Armour is a flat reduction per hit (small arms can't hurt heavy plate).
4. **Drivers don't shoot**; milestone 2's passengers don't either (turrets come with construct weapons).
5. **Unpowered vehicles hold still** below 1.5 m/s (a parking brake), so parked and dead cars don't creep.
6. Players are pushed by constructs but never push them (except passive doors); getting run over hurts since milestone 2.
7. Parts snap to sockets in 90 degree steps on a 1 cm grid. (Free placement of a new construct on any surface came
   later: [New constructs](#new-constructs-free-placement).)
8. Constructs reset with the match (no saving); everything is in memory on the host.

## Milestone 2: joints, logic and the drivetrain

### Joints

Every joint is still one number on its body (reduced coordinates: an angle, or a piston's extension), so the two
pieces it links, the parent segment and the segment hanging on its leaf, can never drift apart or explode, and a joint
costs a few multiplies a tick. A **moving joint** has a motor: each tick it wants a speed (a turntable: its input times
its speed; the rest: towards the position its input asks for, at most its speed) and pushes for it with at most its
strength, against the **load**: gravity on everything the joint carries (its segment, every segment hanging on that,
and 85 kg for each player standing on it) along its motion. A lift whose load is more than its strength sinks; one under
it holds where it is. Limits clamp it; a value that stops being a number is put back to rest. The construct's mass and
centre of mass stay those of the rest pose (cheap; a swinging arm doesn't rock its base yet).

| Joint | Input (the wires into its part, added up) | Unwired |
| --- | --- | --- |
| Passive hinge (`Hinge`) | On: **locked** (Interact refused, pushes ignored) | Swings, latched open or shut by Interact |
| Motor Hinge | 1: to `max`, 0: back to rest, -1: to `min` | Interact opens and shuts it |
| Turntable | -1..1 of its speed (a lever: back, hold, forward) | Holds still |
| Piston | 0..1 of its travel | Interact pushes it out and pulls it in |
| Steering Pivot | -1..1 of its limit | Follows the driver's steering |

Wheels steer only on steering pivots now (milestone 1 steered every wheel ahead of the centre of mass): a suspension
bolted to a pivot's leaf, and the wheel on it, turn with it, and its axle sets the tyre's direction. Caps: 8 joints a
construct, 128 in all.

### Logic

A logic part puts out a signal: on/off (1/0) or a number. **Every wire carries the value its source had last tick**:
each tick works out every part's next value from last tick's values, then they all change at once. The order parts are
worked in never matters (bodies in list order, parts in index order, on the server), a chain of N parts takes N ticks
(a 60 Hz tick is 17 ms), and loops are fine: a NOT wired into itself is a clock, on one tick and off the next.

| Kind | Puts out | Setting (R with the linking tool) |
| --- | --- | --- |
| Seat (`seat` role) | 1 while someone sits in it | - |
| `button` | 1 for half a second after Interact | - |
| `switch` | Interact: 1 or 0 | - |
| `lever` | Interact moves it: middle 0, forward 1, middle 0, back -1 | - |
| `plate` (Pressure Plate) | 1 while a player stands on it | - |
| `reader` (Input Reader) | 1 while the sitter of the seat wired into it (unwired: the driver's seat) holds its key | key: fire, aim, reload, jump, sprint, forward, back, left, right |
| `speed` (Speed Sensor) | its construct's speed, m/s | - |
| `proximity` | 1 while a player is within its range | 3, 6 or 12 m |
| `and` / `or` / `not` | every input on / any on / none on | - |
| `timer` | while enabled (nothing wired in, or an input on): 1 and 0, each for half its period | 0.5, 1, 2, 5 s |
| `delay` | its input, once the input has held its new value for the delay (shorter blips are lost) | 0.25, 0.5, 1, 2 s |
| `toggle` | flips each time its input comes on | - |
| `compare` | 1 when its inputs add up to more than the threshold | 0.5, 1, 5, 10 |
| `scale` | its inputs added up, times the factor | x -1, x 0.5, x 2, x 0.1 |
| `lamp` / `horn` / `brake` | (no output) lights / beeps / brakes the wheels while its input is on | - |

Outputs act the same tick they are worked out: joints (above), the **engine** (a signal wired into it is its throttle,
-1..1, and it drives with nobody in the seat), lamps, horns, brakes, and a passive hinge's lock. A part takes at most 4
inputs (an input reader, a timer and a delay 1).

### The linking tool

Key 5. Fire on an output part, then fire on an input part of the **same construct**: the server checks and wires them,
or refuses with the reason: "take out the linking tool (5) to wire" (bare hands, a gun, the wrench or the welder),
"get out of the seat first", "too far away", "a Foundation has no output", "a Switch takes no input", "an input reader
reads a seat: wire it from one", "those are already wired", "a Delay takes at most 1 input", "a wire reaches at most
8 m", "at most 64 wires a construct". Aim on a part cuts every wire on it; R on a logic part cycles its setting. Wires
are drawn only while the linking tool is out (through the parts, green while their signal is on, a yellow block at the
input end). Wiring is instant (fitting a part is what takes time).

### Passengers, riding and running people over

- The first working seat of the body drives; any other seat carries a **passenger**, pinned to it like the driver. A seat
  holds one player (`SitterPlus1` on the part, so it survives parts being renumbered); a seat that breaks or falls away
  stands its sitter up. Seated players' fire, aim and reload keys go to the server (`CAct.SeatKeys`) for input readers.
- A player standing on a part is **carried** by the body's motion and, sideways, by every joint under their feet (a
  turntable, a swinging deck); a rising lift lifts them by the step-up rule. They count towards that joint's load.
- **Run-over damage** (`ConstructWorld.BlockPlayer`, `Game.SandboxRunOver`): only the **construct's own motion** counts:
  the velocity of its part at the contact (the body's motion, linear and turning, plus every joint's up its chain: a
  swinging arm, a turntable) along the contact normal towards the player. The player's own velocity plays no part, so
  sprinting into a parked car only stops you, and landing on a parked car is a landing (normal fall damage only). At up
  to `Game.RunOverSpeed` (4 m/s, tunable) the part only pushes the player along at its speed; faster, it does
  `(speed - 4) x 9 x sqrt(mass / 500)` damage (the mass factor 0.4 to 2.5), at most once in 0.6 s, and knocks them out
  along the contact and up, harder the faster and heavier it is (`Game.RunOverKnock`). The same rule holds for moving
  joints and wrecks. The driver gets the kill (shown as a melee kill), or nobody for a driverless construct. The
  "Construct run-over" self-test covers each case.

### Seats: riders as part of the construct

- **Seat types** (`SeatKind`, from where the seat is): the drive seat is the **driver's**; a seat on a moving joint's
  segment (a turntable, a hinge) is a **gunner's**; any other is a **passenger's**. The **Gunner Turret** blueprint is
  the Turret with a gunner's seat on its arm.
- **The seat frame.** Every frame, on the host and every client, after the render states and before the camera
  (`Game.UpdateSandboxSeats`, GameSandbox.cs), each seated player is put in their seat exactly as the construct is drawn
  this frame (`ConstructRender.SeatPose`: the body's blended pose, every joint up the seat's chain blended, the seat's
  turn): the feet at the seat's `at` point and the whole body in the seat's frame, so it tilts and turns with the
  construct and with a turntable or hinge under the seat. There is nothing to lag, jitter or slide: the rider is a
  seat-local pose composed with the construct's own drawing. (A client blends the construct poses there too, before the
  camera is set.) The server still pins the simulated player to the seat each tick for hit boxes and reach.
- **The seated pose** (`LegAnimator` seated legs): hips on the cushion, thighs forward, knees bent, feet forward on the
  floor, leaning back a little; no gait, no foot rays, no footsteps or step sounds, no head bob, no sprint arms. The upper
  body turns towards the look (driver 26 degrees, passenger 46, gunner 63 either way). The **driver** holds the
  **steering wheel** (drawn in front of the drive seat, turned by the steering) with both hands and has no gun; a
  **passenger** rests the gun across the lap; a **gunner** holds it up at their look.
- **The look** is relative to the vehicle: the local view turns with the seat (like a turning platform's), and the look
  stays within 100 degrees of the seat's facing for the driver, 120 for a passenger, all the way round for a gunner.
  The camera is at the seat's head (the eye height along the seat's up).
- **Network (protocol 80).** A seated player's snapshot entry carries the seat (construct id and seat part, in the
  mantle extension with mantle ticks 0) and a yaw relative to the seat's facing instead of a world one; a client
  composes it with its own drawing of the construct. The seat-relative look comes from the seated client itself
  (`CAct.SeatLook`, unreliable, on a change, at most ~30 a second and every 0.25 s), so it is exact however fast the seat
  turns; until the first one arrives the server works it out from the world look. The world position stays in the entry
  (a fallback for a construct the client doesn't have yet).
- **Getting in and out** keeps its rules (Use on a free seat; F gets out, beside the seat if clear).
- **Dying seated**: the ragdoll starts in a seated pose at the seat, with the construct's velocity there, and goes limp
  (no death reaction).
- The **"Seated riders"** self-test: in a game on the yard a driver and a passenger in the wired buggy and a gunner on
  the Gunner Turret stay within 4 mm of their seats' local offsets (feet and eyes, every frame between ticks) while the
  buggy drives at up to 12 m/s and turns 140 degrees and the turret turns; no walk cycle or footsteps; the look limits; a
  seated ragdoll; no allocation; over a loopback two clients agree on the seat, the offset and the seat-relative look.
  `--sandbox-shots` writes `smoke_sandbox_seated_*.bmp` (riders, the driver close up, the passenger, the driver's view,
  the gunner).

### Decisions made for milestone 2 (the developer may change them)

1. **Joints stay reduced coordinates**: a joint is a number on one body, not a constraint between two free bodies, so it
   can't come apart or explode; its load is gravity on what it carries, and the body's mass properties use the rest pose.
   Since the joint-collision fix contacts on the parts beyond a joint drive that number directly ([Joint collision](#joint-collision)).
2. **Every wire takes one tick** (the default the brief suggested): order never matters, loops are clocks. Chains of
   many gates are a little slower (17 ms a part).
3. **Wires join parts of one construct only** (a button on a wall can't run a separate gate yet), up to 8 m long.
4. **Steering is a joint**: wheels steer only on steering pivots, which follow the driver unless something is wired in.
5. **The drivetrain is strict**: an engine drives nothing without a transmission touching it and a differential it feeds
   (the scripted build test fits both); only wheels on a differential are driven, the rest roll.
6. **Seats**: the first seat drives, every other is a passenger seat. Passengers don't shoot.
7. **Logic settings** are changed with the linking tool's R (no separate configure tool); controls (buttons, switches,
   levers) work with any hands, as Use. (Since the configure tool: R steps through the old values as presets; every
   setting is in the configure tool's panel.)
8. A **pressure plate** is pressed by players only (not by constructs or loose parts).
9. **The welder and linking tool** sit on 4 and 5, the kill-streak keys (Sandbox has no streaks; the Weapon slot 4
   binding still works too), so no new key binding was added.
10. **Run-over kills** show as melee kills and credit the driver.

## Next: milestone 3 and later

- **Salvage** (done, above). Still to come with it: materials, repair, saving inventories.
- **Physical storage** (done, above). Still to come: carrying limits (the developer's question), more storage parts, saving with the world.
- **Construct weapons** (done, above). Still to come with them: artillery (and craters, decision 16), guided or
  flying rockets, a zoomed gunner view through an optic, enemies manning guns.
- **Resources** and production parts, **enemies** that drive and defend constructs, **factions**.
- Links between constructs and power (done, above).
- Saving constructs with the world, then the large world, streaming and points of interest.
- A bigger map built over several iterations, as the developer asked (bumpers, fenders, gauges, batteries and gears came in milestone 3).

## Milestone 1's list, as milestone 2 left it

- Done: the linking tool and logic parts (seat -> input reader -> piston), motorised hinges, turntables, pistons, steering
  as a joint, transmissions and differentials as parts you can shoot, passengers, standing on a moving deck, run-over
  damage, lights (the Lamp).
- Still to come (above): construct weapons, saving, the large world, more parts, a bigger map (salvage and debris
  expiry came in milestone 3).
