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
(in parts) adds [salvage and the inventory](#salvage-and-the-inventory-milestone-3): taking what survives off wrecks, wrecked parts (hauled to a [Recycler](#salvage-haul-decision-31) for materials)
from what was destroyed, building from counted parts, and wrecks cleared away when left alone; and
[construct weapons](#construct-weapons-milestone-3): guns put together on a construct from the weapon builder's parts or
as heavy guns, fixed or on turrets, fired by a gunner or by logic (sentries), fed from ammo boxes that cook off, with the
weapon builder's penetrating, explosive and incendiary rounds.

A Sandbox world is saved and loaded as a whole: docs/sandbox/WORLD-SAVES.md.

## Playing it

Host a game on **Construct Yard** (Sandbox pack) and pick **Sandbox** (a Dev-only mode, like the template). The yard has a
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
| Aim (hold) | With a tool out: take the part under the crosshair off (into your hand; with your hands full, into the backpack if it fits there, else dropped where it was, with a HUD line). |
| T | Drop the part in your hand. |
| Tab | The **inventory** (below): any part into your hands, any blueprint in front of you, a tool out. Tab again or Esc closes it. Sandbox has no scoreboard. |
| Middle mouse (hold) | The **tool wheel** (below): flick to a tool, let go to take it out; let go in the middle to cancel. |
| W S A D, Space | Driving: throttle, reverse (brakes first), steering, brake. |
| Fire / Aim / R (seated) | Held keys the construct's **input readers** can read (drivers and passengers don't shoot). |
| Physics gun (tool wheel, north-east) | Hold Fire to grab and carry a construct, wreck or loose part; wheel: distance; F + mouse: turn it (Shift snaps); Aim: freeze it; R twice: unfreeze yours ([The physics gun](#the-physics-gun)). |
| Hands | (The tool wheel, south.) Fire takes the loose or stored part under the crosshair into your hands; Aim drops what they hold where you look. [Hands](#hands). |
| Linking tool | Routed links ([Routed links](#routed-links-decision-20)): point the tool wheel at it for its **outer ring** (route links, delete / move node / re-route, highlight all; no link type: the port gives it, [Link type from the port](#link-type-from-the-port-at01); [The outer ring](#the-outer-ring-a-tools-options)). Fire on an output, then Fire on an input: the game routes it round the parts and a ghost shows the route (Fire for a node of your own on the way; a red node: hold Fire and scroll to adjust it; hold Left Shift for free routing, no port snap); Aim or Backspace takes the last node off, Esc cancels. Aim on a part with nothing routed: cut its data links. R on a logic part: its next setting (a preset). |
| Ground drill | (The tool wheel, between the configure tool and hands.) Fire digs the aimed ground into carried dirt, stone and sand; Aim puts it back. Outer ring: stone, dirt or sand ([Sculpting](TERRAIN-DIGGING.md#sculpting)). |
| Configure tool | (The tool wheel.) Fire on a part: its settings panel. Shift+Fire: select more parts of the same kind (changed together). Aim: clear the selection. [The configure tool](#the-configure-tool). |

### Turning the ghost

Board item dev-sandbox-arrow-rotate-ar02 (`Source/Constructs/GhostTurn.cs`). Every ghost (free placement on a surface
and attaching to a socket) turns with the same keys, each a quarter turn relative to the camera:

- **Left / Right** (the arrow keys): about the vertical; Left swings the part's front to your left, whichever way you or
  the construct face.
- **Up / Down**: about your view's right axis; Up tips the part's top towards you (its front rises), Down away.
- **Shift + Left / Right**: roll about the axis pointing away from you; roll left takes the top to your left.
- **R**: back to the default orientation: smart rotation's guess ([Smart rotation](#smart-rotation)); pressed while
  at a guess, the next best guess.

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

### Smart rotation

Board item dev-sandbox-smart-rotation-sr01, design decision 22 (`Source/Constructs/SmartRotation.cs`, the weights in
`Constructs/SmartRotation.json`). The ghost on a socket no longer starts at a fixed "upright, facing -Z" turn: every turn
the held part can take there (each of its own sockets times four spins, one per distinct turn; turns that are in the way
are never candidates) is scored by context and the best is the ghost's first guess, shown at once. **R** goes back to the
guess, and pressed while the ghost is at a guess it steps to the next best, so a wrong guess is one press away; the arrow
keys turn it as before ([Turning the ghost](#turning-the-ghost)). The signals, each weighted in the JSON file:

- **The vehicle frame**: forward and up from the driver's seat, else the first seat, else the root part (an anchored or
  pinned build takes the world's up), so a chassis built at any angle works. `upright` scores the part's top along that
  up, `front` its front (-Z) along that forward.
- **Facing hints** (`facing`): a part file's `"faces"` / `"faceAxis"`, else its role's default: seats and guns face
  forward, wheels put their outer face (+X) away from the centre plane, panels their front (+Y) and doors theirs (+Z)
  away from the construct's centre, gauges their dial towards the nearest seat. `back` and `up` are there for tail lights
  and exhausts.
- **Mirror symmetry** (`mirror`): if the same part sits at the mirrored spot across the centre plane (the plane through
  the frame part's centre, square to the vehicle's right), the turn that mirrors its turn (its face and its top mirrored)
  wins: the right wheel matches the left.
- **Repetition**: the same parts next to it (`neighbour`, by the share of them with that turn: a row of panels or lights
  lines up), and the turn the player last chose for this part (`lastChoice`), relative to the vehicle frame.
- **Corrections** (`learned`): when the player places a part turned away from the guess, that turn is remembered for the
  part, the socket kind and the side of the vehicle the socket faces (relative to the frame) and preferred next time.
  Free placement keeps its own.
- **Clearance**: a blocked turn is never listed; covering another part's free socket (`coveredSocket`, each, at most four;
  a socket the part meets with one of its own doesn't count, it welds) or a facing part's face right against another
  part (`facingWall`) scores lower.

Ties break by the old fixed order (upright, then facing -Z in the construct's frame), then the lowest variant, so the
same build always gives the same guess. Free placement uses it too: the new construct's frame is the vehicle frame, so a
part in hand starts upright facing the look, or the way it was last set down (R steps through the 24 turns in score
order). A blueprint step's `"spin": "auto"` takes the context guess (no memory). The physics gun's Shift snap is not
affected: it snaps a held construct to 45 degree steps, not a part to a socket.

The guess is the client's alone: the client sends the variant (own socket and spin in the fixed order) it shows, and the
server checks it exactly as before, so nothing changed on the wire. Ranking runs only when the aimed socket, the held
part, the construct's part count or the setting changes, never per frame, and allocates nothing (at most 24 candidates).
The parts menu's **Smart rotation** box (on by default, saved as `smart_rotation` in settings.txt) turns it off for the
fixed order. The corrections are saved in settings.txt too (`smart_rotation_learned`, part names, at most 256, the
oldest going first); the last choices last the session.

**Self-test**: the **Construct smart rotation** section (`Source/App/SelfTestSmartRotation.cs`): a left wheel's guess
puts its outer face out to the left, and with the left wheel turned off its guess the right wheel's guess mirrors it; a
seat on a chassis turned 90 degrees faces the chassis's front where the fixed order faced -Z, and a second seat faces the
first's front; a third panel continues its row's turn; after one correction the next same seat (on another construct)
takes it, also after a settings.txt round trip, and free placement learns the same way; with forward blocked the guess
is a free upright turn and every listed turn fits; two identical builds rank the same; a blueprint's `"auto"` step; the
weights file loads and a bad one is refused, as is a bad `"faces"`; ranking allocates nothing.

Choices made (the developer may change them): the corrections are saved in settings.txt (Settings fitted: one line)
rather than kept for the session only; R both resets and cycles (no new key); the centre plane passes through the frame
part (the driver's seat), not the build's middle, so a half-built car's mirror is stable; the context for a correction is
the part, the socket kind and the side of the vehicle the socket faces; the weights are the file's (upright 2, front 1,
facing 4, mirror 6, neighbour 5, last choice 3.5, learned 8, covered socket 1.5 each, facing a wall 3).

### The tool wheel (decision 17)

The developer's decision 17 (2026-10-29): players pick their tools from a radial menu. Hold **Tool wheel**
(`GameAction.ToolWheel`, the **middle mouse button** by default, rebindable; added by binds version 10. Q was taken by
Swap weapon, and the middle button was free everywhere) and nine segments appear around the middle of the screen,
clockwise from the top: **wrench**, **physics gun** (it took the hoist's place, [below](#the-physics-gun)), **welder**, configure tool, **ground drill** ([Sculpting](TERRAIN-DIGGING.md#sculpting)), **hands** ([below](#hands); the guns are on its outer ring), **remover** ([below](#the-remover-tool-board-item-rt01)), **linking tool**, **paint tool** ([below](#the-paint-tool-board-item-pp01)). The ninth segment moved the others by up to 20 degrees, so the old compass flicks still land on them
([The configure tool](#the-configure-tool) has no number key: the wheel or the inventory's Tools tab; a slot with no tool in it is greyed out with "coming later", and letting go on one says so and changes nothing). While the key is held the view stands still and the
mouse moves the wheel's pointer instead (accumulated, held within 70 px of the middle, so a small flick turns it to
another segment); the segment is picked by direction (a 22 px dead zone in the middle). The highlighted segment gets an
orange arc and its name shows in the middle (only the name, fitted to the centre circle); its favourite key and hint ("key 5: wires, settings") show in a tooltip under the wheel; the tool in your hands is marked
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
shapes from lines and boxes: an open-jaw wrench, a torch with sparks, two terminals and a wire, an open hand, a hook on a
chain, three sliders, a handset with waves, a drill); `Source/App/Program.SandboxWheel.cs` hooks it into the frame.

Choices made for the wheel (the developer may change them): the middle mouse button, not Q (Q swaps weapons); the wheel
sits at the screen centre (the cursor is captured, so "around the cursor" is the centre); the tools still to come are
shown greyed rather than left off (eight segments read cleanly as a compass, and they show what is planned); it is
closed in a seat (seated keys belong to the construct's input readers); no stick support yet (no Steam Input in the game).

#### The outer ring: a tool's options

Board item dev-wheel-outer-ring-wo01 (the developer's design, 2026-10-30) replaced the linking tool's separate link wheel
page (and its rings opened by resting the pointer) with a concentric **outer ring**. Pointing at a tool that has options
shows a second ring around the inner one at once (it grows out in 0.08 s); the inner wheel stays and the tool stays
highlighted. Its segments sit in an arc centred on the tool's direction, 30 degrees each, or round the full circle when
more than fit in 300 degrees. With the ring showing the pointer may go further out (to 70 + 66 px); past 70 + 24 px
(a band that keeps the inner pick) it picks the option in its direction. **Letting go on an option** takes the tool out
with that option; **letting go on the tool** (the inner segment) takes it out with its last-used option (marked green on
the ring); letting go in the middle still cancels. Outside a partial arc the pointer turns the inner ring as usual (and
is pulled back within 70 px when it leaves a tool with a ring). The middle shows only the name of what is under the
pointer (the option's, else the tool's, else "Tools"), at 20 px a scale, shrunk to fit the centre circle (down to 12 px),
else cut short with "..."; hints go in the tooltip under the wheel.

The **linking tool** has nine options, in an arc centred on it (270 degrees): data cable, power cable, pipe, drive shaft,
chute, delete links, move node, re-route, and highlight all (a toggle, shown with a green dot while on). Picking a type
also goes back to routing links. (Since board item at01 the ring holds the modes only, clockwise: Delete links, Move node,
Route links, Re-route, Highlight all; Route links sits in the middle, straight out from the tool, so a flick out along
the tool lands on the default and not on Move node.) **Route links** (a new connection) is the default: the tool comes out in it on a plain
take (its key, or letting go on the tool itself) and goes back to it when put away; a mode picked on the ring while
another tool is out is kept until the linking tool arrives (the switch is the server's, a round trip later; before, the
first frame of that wait put it back to Route links, so the tool came out in another mode than the ring had marked),
or dropped after 2 s if it never comes (`SandboxLinkWheel.ToolOut`). The ring's green "current" marker is read from the
mode, so the two always agree; "Sandbox tool wheel outer ring" (LinkDefaultMode) drives it through the real input, and checks that straight out from the tool is Route links.

**The third tier** (board item dev-wheel-third-tier-pipes-wt01): an option may have sub-options of its own
(`RadialMenu.Item.Options` on the option; `IToolOptions.ChooseSub(option, sub)`, optional, called for a pick on the
tier, with `Choose` kept for letting go on the option itself). While the pointer is on such an option a **partial arc**
grows out round the outer ring, centred on that option's direction, 25 degrees an item (`RadialMenu.SubStepMax`; five
items span 125 degrees). Flicking out past the outer ring (70 + 66 px, plus 8) onto one highlights it; letting go there
gives it. The pointer may then go 56 px further out (`SubDepth`). Hysteresis: a sub-option is entered 8 px past the
outer ring's edge but kept down to 2 px past it, and the arc keeps its option while the pointer is out past the edge in
the arc's direction; moving inward, or round onto another option, closes the arc (and pulls the pointer back in). The
pipe was the first user (styles for fuel, water, oil and steam) until decision 28 gave the game one pipe for every fluid; the
linking tool's **Pipe** is now a plain ring option, and the mechanism waits for the next tool with sub-options. The wheel follows accumulated movement, so a stick would reach the arc the same way (tilt further out).
Nothing allocates per frame (labels are made once) and nothing is on the wire. The choice is this PC's only;
taking the tool out goes through the same request as before, so nothing on the wire changed. The widget is generic
(`RadialMenu.Item.Options`; a tool gets them through `IToolOptions` and `SandboxToolWheel.SetOptions`), so the welder's
weld / cut and the physics gun's modes can use it when they have modes, and a third tier for other tools (actuator
sizes, welder modes) works the same way (the self-tests give the welder slot a test set with a third tier).

Choices made for the outer ring (the developer may change them): the pipe styles first sat on the same ring as the other
types, then on the third tier above, and are gone with decision 28 (one pipe); the edit modes and highlight all are options too; the ring shows the moment a tool is pointed at
(no rest needed); 30 degree option segments; "Tools" is gone from the linking tool (the tool wheel always opens now).

### Hands

`ConstructTool.Hands` (wire byte 12, `Source/Constructs/ConstructEditsHands.cs`, `Source/App/Program.SandboxHands.cs`), the tool wheel's
south slot. It takes things and puts them down; it never fits or removes a part (`ConstructBuild.ToolFits` is false for it, and
the Replacer refuses it), so fitting stays the wrench's and the welder's. What the hands hold stays there when the tool changes.

- **Fire** (one thing a press): a held part goes into the lit storage slot under the crosshair; else the stored part in a slot
  comes out (`CActKind.TakeStored`, as F does); else the loose part comes into the hands (`CActKind.HandTake`: `PickUp` plus
  the contested rule, so F's rules for reach, `ConstructBuild.UseReach`, apply). Bins and hoppers stay F's.
- **Refused, with a short line**: physics-only parts and extrusions (decision 21: "use the physics gun"), a wreck lying alone, a
  loose store with something in it, a body someone holds with the physics gun, a defended enemy's part (decision 19). A fitted
  part names its tool. The look line is `Lamp - LMB: pick up`; a refusal shows red.
- **Hands full**: the part goes to the backpack if it fits there (the give rule, as with F), else it is refused and stays. It
  never swaps.
- **Aim**: `CActKind.HandDrop` sends the crosshair's point. The server sets the part down just short of it, at most
  `UseReach` away, or at the nearest clear spot back along the look, else straight in front; with no room at all it stays in the
  hands. It leaves with the player's speed in the world (`Game.SandboxCarry`: their own, plus the construct they stand on)
  and a 1 m/s toss along the look. A hose's receiver in the hands reels back first. T still drops as before.
- **Outer ring**: primary gun, hands, secondary gun. Hands is in the middle, and letting go on the slot itself is always hands.
  1 and 2 still take a gun out.
- No model: only the arms show, open; a carried part is in both hands. Grab and Drop sounds play when the hands fill or empty.

Choices made (the developer may change them): hands replace the old "Hands / gun" slot instead of a tenth slot, which would
have moved every compass flick, so a flick south no longer takes the gun out; full hands use the backpack rather than swap;
F is unchanged, so on a loose part or a slot it does what Fire does, except that F runs no contested check on a loose part;
you still spawn with the gun out (`SandboxHand.Empty`), not hands.

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
- The **ground drill** (its bit spins while a stroke runs; [Sculpting](TERRAIN-DIGGING.md#sculpting)).
- Still to come, with models ready for when the tools exist: the **hoist** (a winch with a
  hook on a chain) and the **scanner** (a turning emitter and an aerial; a beam while it scans).

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
| `tool` | `wrench`, `welder`, `linker`, `physgun`, `configure`, `paint`; or one still to come: `hoist`, `scanner`, `drill`. One file a tool. |
| `hold` | How it is turned in the hand (degrees: pitch, yaw, roll), about the grip. |
| `boxes` | Up to 24: `size` (and `at`) or `min`/`max`, each 2 mm to 60 cm a side, `color`; `"glow": true` draws it unlit and bright (flickering in use, a slow pulse otherwise); `"spin": true` turns it about the `spin` section's `axis` through its `pivot`. The boxes must touch into one piece. |
| `grip` / `support` | The strong hand (`at` within 15 cm of the origin) and the off hand (optional: without it the off hand rests): `at`, `rot` (degrees), `shape` (`wrap`, `under`, `cup`, `flat`). |
| `tip` | Where sparks, the light and the beam come from. |
| `use` | While it works: `motion` (`ratchet`: turns `amount` radians about `axis`; `jitter`: shakes `amount` metres; `push`: jabs forward `amount` metres; `sweep`: turns side to side; `none`) at `rate` Hz; `spin` / `idleSpin` (radians a second for the spin boxes); `sparks`, `light`, `beam` (colours) and `beamLength` (metres). |

A bad file is refused with the reason (`--check-construct-parts` lists the tools too) and that tool is drawn as a plain
grey block.

Choices made (the developer may change them): the wrench is one-handed and upright (the off hand rests), the welder and
scanner two-handed; a carried part goes in the off hand, scaled down (a frame block would hide the view at full size);
putting a tool away brings the gun up with its equip time before it can fire; the welder's light shows only on lit maps
(the dynamic lights live in the light grid); the tools still to come have models but stay greyed on the wheel.

### Noclip

Creative worlds only (the map's `infiniteParts`; the same test as the physics gun and the remover). The flight is
`NoclipMove`, a branch of `World.Simulate`, so the server, the client's prediction and its replays run one step.

- **The wish is a level, not an event.** The key sets `Player.NoclipWanted`; every input carries it (`InputCmd.Noclip`, a
  trailer byte of `Msg.Input`, a bit per command; protocol 126). A lost packet costs nothing and the client goes in on the
  very input the server does. `Game.NoclipFilter` checks the wish wherever an input is simulated, so a hand-made packet
  flies nobody outside creative Sandbox. `MoveState.Noclip` is what is actually happening; a snapshot's fast entry says so
  (extension kind 253), which is how a client's replay and a third peer know.
- **Speeds.** 10 m/s, Sprint 40 m/s. Sprint held on keeps building to 240 m/s in about 5 s, worked out from the velocity alone,
  so it needs no state or wheel and a snapshot carries it.
- **Bounds.** The map's rectangle, 200 m under the lowest ground to 2 km over the highest; a shaped map's outline isn't
  enforced until leaving. On a planet: any depth, up to the sky's edge (the map's half size, as for walking). A flyer can't
  leave the space bubble; a ship does that (docs/space/SPACE.md).
- **Nothing touches a flyer.** `ZBlockPlayer` returns early, so no construct block, ride frame, run-over or body push; blast
  pushes and a map's fall-kill floor skip them too. Bullets still hit. Going in leaves any ride frame.
- **Leaving.** `NoclipMove.ExitSpot` (run from `ZBlockPlayer`, with `ConstructWorld.BodyIn`): `World.ClampToArena` first (the
  map, the ground, and the ceiling walkers have: highest ground + 60 m), then the first room for a standing body straight up,
  to 800 m. Then you fall; players take no fall damage.
- **Seats.** A seat ends noclip (it does not suspend it), and getting up stays on foot. Grab-on is refused in flight.
- **The key.** `GameAction.Noclip` (Sandbox context) shares V with Melee (`Keybinds.Overlap`). Where the two share a key in a
  creative world it flies and doesn't swing (`Program.MeleeInput`); elsewhere V is Melee. Crouch is held for down in flight, not
  toggled (`Program.NoclipButtons`).
- **A third peer** sees a floater: `RenderGrounded` off and `RenderVelocity` zero (no running legs, no 240 m/s ragdoll), and the
  snapshot extrapolation skips the arena clamp.

### The physics gun

Board item dev-sandbox-physics-gun-pg01 and decision 19. A Sandbox tool (`ConstructTool.PhysicsGun`, its wire byte 8,
after the tools still to come) on the tool wheel's north-east segment and in the inventory's Tools tab, with its own held
model (`Constructs/Tools/PhysicsGun.json`: an amber projector with a spinning coil and three prongs, two-handed; its
coil spins and its tip lights the map while it holds something). The keys are the existing actions, so they rebind:

| Key | With the physics gun out |
| --- | --- |
| Fire (hold) | Grab the construct, wreck or loose part under the crosshair (30 m reach; 12 m in survival). Let go to drop it (it keeps its speed: a flick throws it). |
| Mouse wheel | While holding: pull it in or push it out (12% of the distance a notch, at least 25 cm; 1.2 m nearest). |
| Interact (F) held + mouse | While holding: turn it (about up, and about your right); the view stands still meanwhile. Sprint (Shift) snaps its WORLD turn to whole 45 degree steps about the world's axes (or those of another construct within 2 m; the key line says "snap: world" or "snap: construct N"): a notch of mouse left/right is 45 degrees about up, up/down about the world horizontal axis nearest your right, and while Shift stays down the piece keeps its world turn as you turn the view. |
| Aim | While holding: **freeze** it where it is and let go. |
| Reload twice within 0.35 s | Unfreeze everything you froze (the host holding Sprint: everything, anyone's). The HUD says how many. |

- **Holding**: the grab point keeps its offset on the thing, and the thing keeps its turn relative to your look's yaw
  (turn round and it swings round with you). Each tick the server sets the beam's point (your eye plus your look times
  the distance) and the wanted turn; inside the physics step, after gravity, a spring (`ConstructGrab.Drive`; pr02 put
  back the spring of before pr01, stronger) pushes the body's velocity towards the one that closes the grab point's gap
  over 0.06 s (at most 30 m/s, and no faster than it can brake within the gap, so it doesn't overshoot) and its spin
  towards the one that closes the turn over 0.07 s (at most 15 rad/s), by no more than the beam's strength allows that
  tick plus gravity (so it hangs in the air). The grab point is carried, not swung: the centre takes the grab point's
  wanted velocity less what the spin gives it. The contacts are solved after it, so the held thing stops on the map and
  pushes loose things; beside another construct its step is also limited to 0.2 m a tick and tried first against that
  construct's pieces (`HeldSweep`), so it stops at it, slides along it and goes into a slot it fits. That test skips what it
  is coupled to and any pair of pieces that overlap already (left to the contacts: pr02, where a trailer on its ball blocked
  every step, so the held thing only turned with the look), and of a coupled pair the held one leads on the hitch while the other follows (`Contact.KinA/KinB`). A held body never sleeps. The holder's own
  client draws the piece at the pose its aim asks for now (`ConstructGrab.Predict`, no protocol change), never further from
  the server's pose than 15 cm plus 0.2 s of the motion in hand; other players see the server's motion.
- **Strength** (pr02; were 200 kN, 8 kN and 40 m/s2): 1.5 MN in creative (at most 120 m/s2, so a 15 t truck gets 100 m/s2), things up to 60 t (a tier 5 Foundry is 48 t); survival 20 kN, up to 500 kg (heavier: "too
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
- **Left**: a search box (name, category, section, role, size or description; it covers every category and keeps the
  size filter; Enter takes the best match) and the **category list**: "All", then the categories, by what a part does.
  The picked category opens its sections; the rest stay shut, so the list fits at 1280x720. Counts follow the size
  filter, and a category the filter empties is greyed out. The options (keep open after picking, smart rotation, snappy
  building, placement) fold away under **Options**. Below them: the tile size slider. Blueprints are grouped as vehicles
  (a seat and wheels), machines (joints), armed or structures; tools as in the game or still to come.
- **Size filter** (Parts tab, above the grid): All sizes, Small, Medium, Large, Capital, by a part's size class. It stays
  on between categories and through a search.
- **Right**: a scrolling grid of square **3D tiles** under category headers and section headers (a search's results flow
  under their category, best first). Inside a section: by size class, tier (so a machine's line reads Scrap to Plasma),
  then name, with the numbers in a name in order (4x4 before 16x8). Each tile is the thing drawn from its boxes at a 3/4
  view with the default light. A tile's dot is the tool it needs (orange welder, blue wrench, green linking tool), its
  badge how many you have, and hovering shows its name, tool, mass, health, armour, fitting time, how many you have and
  its description. Above the grid: what is in your hands, how many tiles are shown, and the **recent** row (small tiles
  of your last eight parts). Each tab comes back to the category it was on for as long as the game runs. Rows out of view
  cost one stretch of space, so listing all 1,500 tiles costs what a screenful does.
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

**The categories** (`Constructs/Categories.json`, read by `Source/Constructs/PartCategories.cs`, used by
`Source/UI/SandboxCatalogue.cs`). One scheme for every part: function first, size as a filter, never a category.

| Category | Sections |
| --- | --- |
| Structure | Beams and frames, Braces and junctions, Plates and sheet, Decks and chassis, Glass, grates and openings, Wedges and curves, Adapters and ballast |
| Buildings | Walls, Wall openings, Floors and foundations, Roofs, Columns and towers, Stairs and access, Doors and gates, Defences |
| Armour and Hull | Armour plate, Heavy armour, Gun ports, Hull sections, Hull plates |
| Drivetrain and Engines | Engines, Modular engines, Gearboxes, Shafts and couplings, Axles and differentials, Thrusters |
| Wheels and Suspension | Wheels, Tracks, Suspension, Steering, Legs |
| Joints and Motion | Hinges, Motor hinges, Pistons, Turntables, Flexible joints, Hitches and chains, Cranes and lifts, Launch and docking |
| Power and Fuel | Fuel tanks, Fuel lines, Batteries, Alternators, Power plants |
| Weapons | Guns, Ammo boxes, Feed chutes, Bombs, Workbenches |
| Weapon parts | Receivers and actions, Barrels and muzzles, Feeds and ammo, Grips and stocks, Optics and utility, Upgrades, Melee, Grenades |
| Production | Drills, Saws, Sawmills, Smelters, Foundries, Assemblers, Extrusion, Components |
| Storage and Logistics | Chests and cargo, Beacons and recycling, Hoses and leads, Conveyors |
| Seats and Logic | Seats and helms, Controls, Sensors, Gauges, Logic gates, Timing, Outputs, Ship systems |
| Stations and Spawners | Build stations, Spawners |
| Lights and Details | Lights, Body panels, Chimneys and stacks |

**Where a part goes.** A part's `"group"` and `"section"` are clues its generator leaves, not the menu: `"group": "Large",
"section": "Engines"` reads "an Engines part" and lands in Drivetrain and Engines > Engines. The file's `rules` run top to
bottom and the first match places the part. A rule's keys must all match: `group`, `section`, `role`, `logic` (the logic
kind) and `name` (any of these words in its name), each one value or a list; `to` is `[category, section]`.

**Placing a new part.** Usually nothing: the rules at the end go by role, so a new wheel, engine, battery or logic part
lands by itself. A part with a new job:
1. Give it the file's `group` and `section` it would have had, as any generator does.
2. If it lands wrong, add a rule above the `given` line in Categories.json (a new section goes in the category's
   `sections` too, which sets its order; at most about 8).
3. Only where no rule can decide, name a category as its `"group"`: the `given` rule keeps it there, with its own
   `"section"` (the Production and Weapons parts do this). A mod's part does it the same way.

`--check-construct-parts` prints every category with its counts (add `--parts` for each section's parts), and fails on a
bad table or a part left in **Other**. A bad file is refused and every part falls to Other. A **role this build doesn't
know** (a new one from another branch) goes by a word in its name (power, battery, gun, turret, storage...) or else into
Other under its own name.

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

It is only in Sandbox, and a map can turn it off with `"partsMenu": false` in its `"sandbox"` section, as Survival in the
lobby does: the key then says why ("the parts menu is off on this map", "... in survival") and the server refuses both requests. The
window allocates nothing per frame while open (every tile's text is made once; the catalogue's key is taken once).

Editing always needs the right tool in hand (decision 2), and since decision 27 the tool follows the part's **function**:
the **welder** attaches and cuts free every **physics-only** piece (welded frames, panels, corners, decks and
foundations, and every part too heavy or bulky to carry: engines, big gearboxes, big wheels, building pieces), with its
three-step [socket weld](#socket-weld-board-item-sw01-decision-26); the **wrench** adds and removes the small parts that
are items (seats, light wheels, suspensions, tanks, fuel lines, small gearboxes, hinges, doors, lamps, sensors, logic and
gun parts) to and from your hands; the **linking tool** does wires and logic settings. With a gun out
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
  overlap checks when building are plain box tests.
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
- Against the map, the ground and other constructs: **every solid box of every part**, in a tree per body
  ([Compound collision](#compound-collision), below): oriented box against box with a contact manifold, speculative
  contacts solved with warm-started sequential impulses and friction. Nothing has a bounding box as its shape: a U nests
  round a post, an L hooks a bar, a beam goes through a gap.
- **The parts beyond joints collide too** ([Joint collision](#joint-collision), below): against the map, other
  constructs and wrecks, with the joint solved together with those contacts.
- **Raycast wheels**: a spring-damper along each suspension (stiffness from the body's mass and the suspension's
  frequency), side grip, drive force shared by the touching wheels, brakes, a friction circle, and tyre forces applied
  most of the way up to the centre of mass (an arcade anti-roll bar) so a buggy turns without rolling over.
- **Joints** (reduced coordinates: each is one number on its body; the moving ones follow the [strength model](#actuator-sizes-and-strength)): a passive hinge is damped, falls under gravity,
  is held inside its limits, latched open or shut by Interact, swung by a player walking into its leaf, and held still
  while its lock is on; the moving joints are driven by a motor (milestone 2, below).
- A body that stays still for 40 ticks sleeps and costs nothing until a hit, an edit, a driver or a nearby body wakes it
  (in a big world it is not even in the step's list: [Scale](#scale-thousands-of-constructs)).
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
- **A plain window, not a menu**: no dim behind it (`Ui.SceneDim`; the parts inventory doesn't dim either, the pause
  menu still does), the match runs on, the part keeps its outline and the previews show; its background is slightly
  see-through. **Drag it** by the title bar or any empty area (kept fully on screen): once moved, every panel opens
  where you left it for the rest of the session; **Snap back** (or a double-click on the title bar) puts it beside
  the part again.
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
| `role` | `frame`, `panel`, `seat`, `wheel`, `suspension`, `engine`, `fueltank`, `fuelline`, `hinge`, `door`, `foundation`; milestone 2: `joint`, `transmission`, `differential`, `logic`; milestone 3: `gun`, `gunpart`, `ammobox`, `feedchute` ([Construct weapons](#construct-weapons-milestone-3)); `generator`, `thruster` ([Big drive](#big-drive)). |
| `tool` | Optional, `wrench` or `welder`: since decision 27 only a hint that the part is structural (`welder`: welded, so physics-only). Which tool fits a part follows from whether it is physics-only (`ConstructBuild.ToolFor`). Old files load unchanged. |
| `attachSeconds` | Time to fit (default 1.5 wrench, 2.5 welder); removing takes 60% of it. |
| `mass`, `health`, `armour` | kg, hit points, flat damage taken off each hit. |
| `material`, `friction` | `steel`, `rubber`, `concrete`...; friction defaults from the material (rubber 1.0, else 0.6). |
| `onBreak` | `detach` (breaks off) or `disable` (stays, stops working; the default for engines, tanks, seats, transmissions and differentials). |
| `boxes` | Up to 32: `min`/`max`, or `size` (and `at`), each with an optional `color`. `"detail": true` boxes are drawn only (no bounds, mass, collision or hit area) and may stick out. At least one box is solid. `"hidden": true`: a solid box that is never drawn. A detail box may have a `"shape"` and its settings ([Structural parts](#structural-parts-the-scale-chart)). `"paintable": false` (or `true`) says whether the paint tool's colour replaces the box's own ([The paint tool](#the-paint-tool-board-item-pp01)); the default is yes, except glass and a wheel's tyre (its solid boxes). `"side": "base"` or `"leaf"` tags a joint part's box with the side it belongs to ([Joint sides](#joint-sides-board-item-js01)); every joint part must tag both. |
| `sockets` | Up to 40 attachment points: `at` (on a face of the solid boxes' bounds, or on a face of one solid box facing out of it: a door's jamb inside an opening, a window's sill), `dir` (`+x -x +y -y +z -z`, out of that face), `kind` (`struct` default, `axle`: a wheel onto a suspension, `leaf`: a door onto a hinge). Two sockets mate when their kinds match and they face each other. |
| `engine` | `torque` (N m at the peak), `maxRpm`, `idleRpm`, `peakRpm`, `curve` (`petrol`, `diesel`, `electric`, `turbine`, `flat`, `steam`), `fuel` (`fuel`, `electric`, `none`), `burnRate` (litres a second at full throttle), `starter` (needs a battery), `starterKJ`, `spool` (seconds), `noise` (0 to 1). [Drivetrain parts](#drivetrain-parts-the-families). |
| `wheel` | `radius`, `grip`, `offroad` (times grip on dirt), `side` (times grip sideways), `roll` (rolling resistance), `drive` (false: a caster), `flat` (goes flat when wrecked), `track` (`length`, `rays`). Needs an `axle` socket. |
| `suspension` | `travel` (m), `frequency` (Hz, at most 6), `damping` (ratio), `maxLoad` (kg a wheel), `active` (a wired signal sets its height), `presets` (`label`, `frequency`, `damping`, `hold`), `ride` (a gas strut), `slew`, `motor` (a hub motor), `path` (the wheel's way through its travel: [Suspension types](#suspension-types)). Needs an `axle` socket. |
| `transmission` | `kind` (`manual`, `sequential`, `automatic`, `cvt`, `reversing`), `gears` (reductions, lowest gear first), `labels`, `gear`, `reverse`, `shift` (s), `efficiency`; a CVT's `range`. |
| `differential` | `kind` (`open`, `locked`, `limited`, `lockable`), `ratio` (the final drive), `efficiency`. |
| `ports` | The part's link ports by function (board item np01, [Named link ports](#named-link-ports-np01)): a list of `{name, type, dir, socket or at + face, fluid, noEffect}`. |
| `drive` | A transfer part (role `transfer`): `kind` (`shaft`, `ujoint`, `cvjoint`, `gearbox`, `chain`, `belt`, `clutch`, `transfercase`, `pto`, `hub`, `stub`), `ratio`, `efficiency`, `ports`, `maxAngle`, `maxTorque`, `front`, `setting`. |
| `fuelTank` | `capacity` (litres; a tank comes full). |
| `seat` | `at`: where the sitter's feet go; `helm`: true, it drives before any other seat ([Large systems](#large-systems)). |
| `hinge` | `axis` (a direction), `pivot`, `min`/`max` (degrees), `leaf` (the leaf socket: what attaches there swings). |
| `joint` | Milestone 2, a moving joint: `type` (`motor`, `turntable`, `piston`, `steering`), `axis`, `pivot`, `min`/`max` (degrees; a piston's are metres, `min` 0 and `max` its travel up to 4 m; a turntable without them turns forever), `leaf` (a struct socket: anything fitted there moves, a door's `leaf` socket too), `speed` (degrees or metres a second), `strength` (N m, or N for a piston). |
| `dock`, `launch`, `conveyor` | [Large systems](#large-systems): a docking clamp's or magnetic lock's block on a cargo logic part, a catapult's or arrestor's, a conveyor's. |
| `logic` | Milestone 2, a logic part: `kind` (below), and for the kinds with a setting `settings` (the values it cycles through), `labels` (what the HUD shows; an input reader's are its keys), `setting` (the starting index), `settingName`. |
| `group`, `section` | Clues for the inventory's rules, `Constructs/Categories.json` ("The inventory"; each up to 32 characters; a section needs a group). A `group` that names a category places the part there. Without them it goes by its role. |
| `yield` | A material bundle (the Material Bundle a broken bin spills): the units the Recycler gives back (its Setting is the material id). Other parts give back by mass and class ([Salvage haul](#salvage-haul-decision-31)). The old `scrap` key is gone (protocol 106). |
| `config` | The part's settings schema (up to 8), the configure tool's panel ([The configure tool](#the-configure-tool)). |
| `walk` | Building pieces: `floor`, `stairs`, `ramp` or `ladder`. Players and bots step up onto it ([Building pieces](#building-pieces)); bots' navigation may treat its tops as ground. |
| `damaged` | `{"below": 0.6, "part": "Concrete Wall 4 m Damaged"}`: below that share of its health the part turns into that stage in place (a wall losing a chunk). The stage needs `"stage": true`, the same role and exactly the same sockets; a chain's shares must fall. |
| `stage` | `true`: a damage stage of another part; not offered in the parts menu or on a rack. |
| `faces`, `faceAxis` | Smart rotation's facing hint ([Smart rotation](#smart-rotation)): `faces` is `none`, `forward`, `back`, `up`, `outward`, `side` or `toSeat`, and `faceAxis` which of the part's own directions is its face (`-z`, its front, by default). Without them the role decides (seats and guns `forward` by `-z`; wheels `side` by `+x`; panels `outward` by `+y`; doors `outward` by `+z`; gauges `toSeat` by their dial's face). |

Milestone 1 shipped 13 parts: Frame Block, Frame Beam, Panel, Corner, Seat, Wheel, Suspension, Engine, Fuel Tank, Fuel Line,
Hinge, Door and Foundation. Milestone 2 adds 25: Deck Plate; Motor Hinge, Turntable, Piston, Steering Pivot; Transmission,
Differential; Button, Lever, Switch, Pressure Plate, Input Reader, Speed Sensor, Proximity Sensor, AND Gate, OR Gate, NOT
Gate, Timer, Delay, Toggle, Compare, Scale, Lamp, Horn and Brake. Milestone 3's weapons add 25 (in `Constructs/Parts/Weapons`:
three receivers, thirteen gun parts, five heavy guns, two ammo boxes and the feed chute) and the Wood Panel.

**No coplanar faces (z-fighting).** Two drawn boxes of a part whose faces lie on the same plane and face the same way
flicker through each other: make one a little different (a detail box inset or proud by 2 to 5 mm; a socket stays on
the solid box's face). `--check-construct-parts` prints the pairs it finds and the self-test section "No z-fighting"
checks every shipped part. The generators in `tools/constructs/` nudge their boxes through `tools/constructs/zfight.py`
before they write a file, so regenerate rather than hand-edit their output.

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

A step's `"spin"` (0 to 3, the fixed order) may be `"auto"`: smart rotation's context guess for that socket and `by`
socket, without any player's memory, so every machine builds it the same ([Smart rotation](#smart-rotation)). A step may carry `"setting": n` (a logic part's starting setting) and `"config": {"period": 2.5, "colour": "#ff2010",
"startOn": true, "setting": "jump"}` (its settings by key: numbers, true/false, colours, option names), and a blueprint may list `"wires": [[from step, to
step], ...]`, each checked by the linking tool's rules. Shipped: **Buggy** (23 parts: rear-wheel drive through a
transmission and a differential, front wheels on steering pivots), **Wired Buggy** (30 parts, 6 wires), **Gate** (6
parts), **Motor Gate** (8 parts, 2 wires), **Lift** (7 parts, 3 wires) and **Turret** (6 parts, 1 wire); milestone 3:
**Gunner Turret** (9 parts, 1 wire), **Hood Gun Buggy** (26 parts, 2 wires), **Sentry** (7 parts, 1 wire), **Gun Bench**
(9 parts, 1 wire), **Target Wall** (8 parts) and **Ammo Dump** (3 parts); the drivetrain families: **4x4 Truck** (38
parts), **Tracked Crawler** (18), **Racer** (24) and **Electric Cart** (23), and the buggies rebuilt on the structural
parts (the Buggy 26 parts, the Wired Buggy 34, the Hood Gun Buggy 27; [Drivetrain parts](#drivetrain-parts-the-families)).
The **RV** (96 build steps, 90 parts after shaft chains migrate, about 2.65 t, 8 wires, a fuel pipe) is a walk-in motorhome: a driven, sprung, steered chassis, a cab, a hinged door with a boarding ladder, a dinette, a bunk over a cargo bay, lamps on switches and a motorised awning. It is generated by `tools/constructs/make_rv.py` and checked by the "RV blueprint" self-test.

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
and `"debrisNear"` (metres, default 12) set when wrecks are cleared away; `"removerInSurvival"` (default true) lets a survival map turn the [remover tool](#the-remover-tool-board-item-rt01) off. Unknown parts or blueprints, and bad values,
refuse the map with the reason.
Only the server reads the section (clients get every construct from the server), so it is not part of the fingerprint.
The Sandbox mode is offered only on maps with this section.

## Physics-only parts and cargo volumes (decision 21)

Board item dev-sandbox-cargo-volumes-cv01, the developer's decision 21 (2026-10-30).

### Physics-only parts

A part is **physics-only** when it is fitted with the welder, when it is heavier than the hand-carry limit
(`ConstructParts.HandCarryLimit`, 40 kg: the backpack's limit), or when its file says `"physicsOnly": true` (light
but bulky pieces: `tools/constructs/building_parts.py` sets it for the 1.25 m wood door, the fence gate, the wood
railings, the 2 m wood beam, the tower brace and the 1 m window glass). The rule reads the welder hint and the mass, never a
list, so every new welder or heavy part follows by itself. Such a part is never an item:

- It can't be held, stowed in the backpack, set into a storage slot or picked up; each refusal says why ("the Frame
  Block is physics-only (welded): it can't go in the backpack - move it with the physics gun or carry it in a cargo
  volume").
- The give rule (`ConstructEdits.Give`) drops it as a body where it was, so every path follows it (salvage, merge
  pop-offs, storage). Taking one off a construct cuts it free as a body at its place, moving as the construct moved.
- The creative inventory (Tab) and the parts rack spawn it as a body a short way in front of you at eye level (clear of
  the map, constructs and players; it then falls and settles). With the physics gun out, the gun holds it. A wrench
  part still comes into your hands, ready to fit.
- It is built onto things with the welder (decision 27): the three-step [socket weld](#socket-weld-board-item-sw01-decision-26)
  (pick its socket, pick where it goes, spin it), or by moving it into place with the physics gun and joining it (F).
  The wrench joins nothing any more: an engine moved out of a cargo volume goes on with the welder.
- The inventory tile has a small boxed corner mark and its tooltip says "PHYSICS ONLY (welded)" or "item"; the look
  text says "physics only" too.
- What a save held in hands, backpacks or storage that is physics-only drops into the world as bodies
  (`ConstructEdits.DropPhysicsOnly`; world saving isn't built yet, so the server runs it at every match start).
- **Creative** (infinite parts on): a wrench fit or removal is instant. Survival and counted parts keep the timed edits.

### Cargo volumes

The **Cargo Volume** part (`Constructs/Parts/Storage/CargoVolume.json`, wrench-fitted, a logic part of kind `cargo`)
is a small control box with a box of space on its construct: its size (`sizeX/Y/Z`, 0.2 to 8 m, 10 cm steps) and
centre (`atX/Y/Z`, from the part, in its frame) are its settings, and `startOn` makes it start frozen. Idle, only a
faint marking of its floor shows; with a tool out the whole box is outlined (ice blue frozen, amber free) and the
control box's lamp shows the same colour.

- **Frozen**: F on the control box flips it; a data link into it sets it on a change of its input (the last change
  wins, so both work on one part). While frozen, every loose body mostly inside the box (its centre of mass in it and
  at least half its part mass) rides with the construct kinematically: no physics of its own, no collisions with the
  carrier or the carrier's other cargo, its mass, centre of mass and inertia added to the carrier's. It is still a
  body: it can be shot, stood on and picked up (an item), and other bodies hit it.
- **Unfrozen** (or the volume broken or gone, or the box resized away from it), everything goes free at the carrier's
  velocity at that point.
- **Things entering while frozen** (the open choice): caught once they have settled, moving with the construct under
  1 m/s relative to it (checked ten times a second); freezing catches everything inside at once, moving or not. A body
  held by the physics gun is never caught, and grabbing a carried body takes it out. Not caught: anything anchored or
  frozen by the physics gun, anyone's seat in use, something hitched to the carrier, another carrier with cargo.
- **The volume tool** is a configure-tool mode: with the configure tool out, every volume near shows a handle on each
  face; hold Fire on one and drag to move that face (snapped to the 10 cm grid; the opposite face stays). R on the
  control box then Fire-drag on the construct draws a new box from corner to corner (its height kept). Fire on the
  control box opens its settings panel, where the size and centre are numbers. Each change is the configure tool's own
  request (`CActKind.Configure`), checked and snapped on the server.
- **Limits**: 4 volumes a construct (a fifth is refused when fitted), 24 carried bodies a construct, 8 m a side.
- **Blueprints**: a `"cargo"` list of loose parts (`{ "part", "at", "turn" }` in the construct's frame) built inside a
  volume that starts frozen; `ConstructCargo.ToJson` writes a construct's cargo back in that form. The **Cargo Rack**
  blueprint is the example. World saving isn't built yet: frozen contents round-trip through the construct message and
  blueprints, relative to the construct.
- **Network** (protocol 93): server-authoritative. A carried body's `CBody` ends with its carrier and its pose in the
  frame of the volume's segment; every machine moves it with the carrier (a client after blending the poses), so a
  carried body costs no pose traffic. The frozen state rides in the part's signal.
- **Cost**: carried bodies skip the physics; the cargo step moves each carried body every tick and looks for what
  entered or left ten times a second; it allocates nothing.

Code: `Source/Constructs/ConstructCargo.cs` (the rules), `ConstructPhysicsOnly.cs` (the item rule),
`Source/App/Program.SandboxCargo.cs` (the handles), `Source/Render/ConstructRenderCargo.cs` (the drawing).

**Self-test**: "Sandbox physics-only parts", "Sandbox cargo volumes" and "Sandbox cargo network"
(`Source/App/SelfTestSandboxCargo.cs`).

Choices made (the developer may change them): things entering a frozen volume are caught once settled (not at once,
not only on the next freeze); a carried body's mass counts at its place in the body (a volume on a joint carries its
cargo with the joint, but the joint's load doesn't count it); the volume's frozen clamps need no power; no cargo in
cargo; the volume tool is a configure-tool mode (no new tool or key); the six building pieces above plus the window
glass are the bulky ones; creative means infinite parts on (as for the physics gun).

### Gravity parts (space phase 5)

The **Gravity Well** (pulls, or pushes on a negative signal, everything near it; on a ship in space, the other ships)
and the **Gravity Generator** (its box gets its own down, the part's -Y; sized like a cargo volume) are logic parts in
`Constructs/Parts/Gravity/`, switched with F or a data signal, needing power. Neither moves its own construct. Code:
`ConstructGravity.cs`; how they reach players, constructs and ships: docs/space/GRAVITY.md.

## Network (protocol 82; 84 for free placement; 86 for the drivetrain's rpm; 87 for glass shattering; 88 for the physics gun)

- `Msg.CBody` (reliable): a construct's whole structure (parts on their 1 cm grid with their logic setting and signal,
  links, joints with their type, lock and wired flags, wires, pose: about 20 bytes plus 14 per part and 2 per wire),
  sent when it changes (an edit, a break, a split, a wire, a setting) and to each joiner; also part health batches, a
  construct gone, and "clear all" at a new match.
- Protocol 81 (salvage): `CBody` also carries the construct's **owner** (a player id, 255: nobody's) and a **damaged**
  flag; `CBodyKind.Inventory` sends one player their own inventory, whole, when it changes and when they join (a flags
  byte (infinite parts), then each part kind held as id and count, then each material held; about 4 bytes plus
  4 a kind; protocol 106 dropped the scrap). Nobody else's inventory is ever sent.
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
- Protocol 101 (body sync, board item pd01): when more bodies move than one pose packet holds, they take turns (the
  next packet starts at the first body left out, `ConstructWorld.PoseCursor`), with room kept for the sleeping ones'
  turn; a body that goes still sends its final pose three more times. A `CBody` Full carries the server tick of its pose,
  so a Full sent again after a loss doesn't put a newer pose back. Every 2 s the server sends the body digest
  (`CBodyKind.Digest`, reliable and in order with the structure: each body's id, `ConstructNet.StructureHash`, sleep
  state and a sleeping one's pose); a client removes the bodies it doesn't list and asks for the ones it holds wrong
  (`CAct.Resync`), which come back whole. "Sandbox body sync" tests it with a real host and client, lag and loss.
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
- Protocol 121 (capital parts): every part index is two bytes, positions in a construct and link nodes three a coordinate
  (`PacketWriter.Cm`, +-83 km on the 1 cm grid), health three; `CBody` part rows are 20 bytes, links 6. A pose carries at
  most 96 joints and 256 signals; a body with more sends a window that turns (first and count on the wire). A body of 128 parts
  or more sends `CBodyKind.Patch` for an edit (`ConstructPatch.cs`): both sides keep its last Full (`PatchBase`) and an edit goes as
  copies from it plus new bytes, at most 10 Hz; a joiner and a resync get the base and the next patch. RakNet splits
  whatever exceeds a packet.
- Protocol 89 (the configure tool): `CBody` ends with the part settings that are off their defaults (a u16 count, then
  part, slot and an f32 value: 6 bytes each; a choice a part already had, like a gear, stays its setting byte);
  `CActKind.Configure` (u16 construct, u8 part count, the parts, u8 setting count, slot and f32 value each) sets them on
  one or more parts of one kind; the tool id `ConstructTool.Configure` (4).

### Joint collision

Board item dev-sandbox-joint-collision-jc01 (a hinged beam sank into the terrain: only the body's own hull touched
anything, the segments beyond a joint were posed from the joint value and went through whatever was there). Now:

- Every part beyond a joint is in that joint's **rigid group** of the construct's collider
  ([Compound collision](#compound-collision)), so it meets the map, the ground and other constructs as boxes, exactly as the
  body's own parts do, and moves with the body *and* with every joint up its segment's chain. Raycast wheels on a steering
  pivot still roll on their rays.
- **The solve** works on generalised velocities: the body's six and one per joint. A contact on a segment carries, for
  each joint up its chain, the velocity a unit of joint speed gives its point; its impulse moves the body (if it is free)
  and turns or slides each of those joints by its inverse inertia. A contact between two constructs does that on both
  sides. The **motors and limits** are constraints in the same sequential-impulse loop (a motor's impulse clamped to its
  strength a tick, a limit one-sided and speculative), solved before the contacts in each pass so non-penetration has
  the last word. So a passive hinge rests on the ground; a motor pushing into a wall stalls at its strength; a strong
  motor driving an arm into the ground lifts its free construct (the arm becomes a leg).
  A locked hinge has no give: its contacts move the body instead.
- **No tunnelling**: every contact is speculative (approach is allowed up to the gap, not past it), its reach covers the
  whole tick's travel using the speed a motor can reach this tick, so a stalled arm keeps seeing what it pushes against, and
  a fast thin piece is tried along its way. A door swung at 25 rad/s (25 m/s at its edge) stops at a 5 cm wall. Joints are
  capped at 25 rad/s (or m/s).
- **Why reduced coordinates still**: the joint stays one number, so the pieces can never drift apart, stretch or explode,
  and it stays the cheapest thing to send (`CPose` already carries each joint's angle and speed; no wire change,
  protocol 79). Contacts drive the joint value directly, which is what constraint-linked bodies would have to work hard
  to match. The approximation: each joint's inertia is its carried mass about its axis (as the motors already used) and
  the body's and joints' masses are treated as independent in the solve, which only shares out how a push moves things,
  never whether a contact holds.
- Not yet: a segment against its **own** construct (joint limits keep the built ones apart), players pushing a moving
  segment back (they are pushed out of it, as before), the body's mass properties following its joints.

### Compound collision

A construct's collider is its real shape. `Source/Constructs/ConstructCollider.cs` (the boxes and trees),
`ConstructNarrow.cs` (the pair tests), `ConstructPhysicsContacts.cs` (who meets what, the points, warm starting),
`ConvexHull.cs` (shaped parts).

- **The collider.** Every solid box of every part is a leaf (`SolidCount`/`SolidBox`, so a merged receiver is its one big
  box). Leaves are grouped by **rigid group**: the body and its freeform welds are group 0, each real joint's parts another
  (`Construct.MoveSeg`). Each group has a bounding volume tree in its own frame, so a joint moving costs nothing: the frame
  moves and the tree with it. A freeform-welded box is a leaf turned in its group. The tree is built on first use after the
  parts change (`Construct.Collider`, in the physics step: a client never builds one) and reuses its arrays.
- **Against other constructs** (`PairContacts`). The two trees are walked together to the leaf pairs within this tick's
  travel plus 2 cm, then the separating-axis test: the three face axes of each box and the nine edge crosses
  (`ConstructNarrow.BoxBox`). A face gives its incident face clipped to the reference face, up to four points; an edge pair
  one. Each point has its gap: above 0 it is speculative (it may close that far and no further), below 0 it is pushed out.
- **Against the map** (`WorldCompound`). `IWorldQuery.Gather` gives the boxes, oriented boxes, mesh triangles, boundary
  pieces and posed movers near the group; each goes through the same tree and test. Trees are `ConstructTrunks.cs`, box by
  box, as before.
- **Against the ground.** `Gather` gives the triangles of the cells under the group, through `TerrainData`, so dug, filled
  and streamed ground and caves all come the same way (a group over more than 900 cells asks leaf by leaf, so a big
  construct on fine ground never walks all of it; streamed tiles come on demand). A triangle is a sheet, not a solid, so its
  contact is always along its own normal (`ConstructNarrow.PieceTriangle`): the piece's lowest face, clipped to the prism
  over the triangle. A plate lying across a ridge rests on the ridge line from both sides, wherever its corners are, and no
  neighbouring triangle's edge can catch a sliding box. A two-sided sheet (a dug wall) pushes to the side the piece is on.
- **Seams.** Two floor slabs side by side: the second slab's side face stands at floor height in front of a sliding box,
  and separating axes alone would stop the box on it. So a side face met within 3 cm of a level top the piece rests on or
  skims (never more than half the thinner piece's half height, so a thin plate edge-on still meets a wall) gives way to that
  top (`ConstructNarrow.Ledge`). A crate rides over a 3 cm kerb; a deck of many plates has no seams.
- **Fast thin pieces.** The reach covers the travel, so a piece meets what it will reach. A thin piece whose travel this
  tick is more than half its thickness, and that met nothing where it stands, is also tried along its way (up to 6 steps,
  `ConstructPhysics.Swept`): what it finds is moved back to now with the gap it closes on the way.
- **Fewer points.** Each rigid group's points against one thing are thinned per normal to the deepest and the eight furthest
  out round it (`ReducePoints`): a capital hull flat on a floor has the contacts of a crate. Past 4,096 candidate points a walk takes every Nth piece, so they stay spread over the body.
- **The solver.** Each contact starts from last tick's normal impulse (a table per world keyed by the pair and the spot on
  the first body, on a 5 cm grid, `WarmEntry`), so a stack carries its weight from the first pass; not from a blow (a
  contact closing fast), which would throw the body back, and not its friction (carried over, a stack's small sways fed
  themselves until it fell). Passes run forwards and backwards in turn (one order turns a face a little every pass).
  An overlap never adds speed: a plain body is moved out by a split impulse (`PushRows`, its own motion for the tick, 3
  passes), and a point inside the 2 mm slop is held where it is (the old 1 cm let each block of a stack rock 2 degrees).
  Only a contact on a part beyond a joint is pushed out by speed (a joint has no second set of numbers).
- **Sleep.** Bodies pressing on each other count their still ticks together (`SleepTogether`), so a stack sleeps as one and
  never drops off from the bottom, block by block.
- **Wheels** stay raycast and out of the ground collision. A wheel's box meets only something loose (and a turning wheel's
  tread carries it). A wheel with no suspension is a plain solid part.
- **Couplings.** A coupling's hitch parts don't collide with each other's construct, and points within 0.7 m of a hitch are
  dropped, as before.
- **Self-collision.** None: parts of one construct never collide with each other (joint limits keep the built ones apart).
- **The physics gun.** `HeldSweep` tries each axis of the step and refuses only a move that makes a NEW overlap of a piece
  with a piece (`NewOverlap`; the 2026-10-31 rule), so a held piece slides along a wall and into a slot 3 cm wider than it,
  and skips what it is coupled to or carried by.

#### What it costs

"Compound collision" (`CollisionCost`) measures these and fails on any allocation (milliseconds a tick, the user's PC runner's
Linux VM, wall time; the same scenes on main before this work, with the old hull points):

| Scene | Before | Now | Contacts before | Now |
| --- | --- | --- | --- | --- |
| 300 blocks falling (no contacts) | 0.19 | 0.17 | 0 | 0 |
| A pile of 200 settling | 2.97 | 2.98 | 3,499 | 1,792 |
| One 96-part hull sliding on a floor | 0.006 | 0.015 | 40 | 4 |
| Eight 96-part hulls stacked, sliding on each other | 2.08 | 0.87 | 502 | 100 |
| One 96-part hull and 40 blocks on half-metre ground | 0.09 | 0.22 | 162 | 255 |
| 300 blocks landing on half-metre ground | | 0.73 | | 1,702 |
| "Construct scale": 2,000 bodies, 195 awake | 2.22 | 1.41 | | |
| "Construct scale": 5,000 constructs, 300 awake | 2.21 | 2.51 | | |

A hull on ground is the dearer case: the ground's triangles under it are looked at each tick, where hull points asked one ray
each. Everything else is cheaper or level, because the trees only ever reach the pieces near something and thinning keeps
the solve short. These rows were measured at 96 parts a construct. At 8,000 (container, full suite): one hull sliding 0.49 ms,
one hull and 40 blocks on half-metre ground 2.5 ms, a buggy on a capital deck 0.53 ms (was 23 to 39 ms with sampled hull
points). Eight 8,000-part hulls stacked cost 84 ms a tick, over the 8 ms budget: each pair walks every piece of the faces that
touch before the candidates are thinned.

#### Shaped parts

A part's contoured shapes are detail boxes over hidden solid boxes. Per shape (`ConstructHulls`; the shapes' table is in
`Source/Constructs/ConvexHull.cs`):

| Shape | Collider |
| --- | --- |
| Wedge | a convex hull from its mesh: a real slope (a block driven at it rides up), not the steps |
| Cylinder, tube | a convex hull from its mesh (a tube's outside; its bore stays solid) |
| Arc | its hidden boxes: not convex, and the steps follow the curve |
| Rounded, bevel | its solid box: nearly the same shape |
| More than 32 corners (the Small Nuke's smooth cylinders) | the hidden boxes |

A hull replaces the hidden solid boxes inside its detail box for **collision and ray hits** (bullets, the look ray:
`ConstructWorld.Raycast` tests the hull, so a bullet over a slope passes). **Mass, inertia, building overlap, ghosts, the outline and
player walking keep the boxes**: a player walks up a wedge's hidden steps (each within a step of the slope, the self-test
holds them to it), which feels like a slope. A hull beats a box in the separating-axis test by testing every face and
edge direction (`ConstructNarrow.HullHull`), and is only reached when something comes close. A part file can say
`"collider": "boxes"` to keep its hidden boxes whatever its shapes (or `"shape"`, the default).

### The frozen outline

A frozen construct is drawn with an icy outline round each part (no more than 24 parts; bigger ones show the frost tint
alone): one box round the whole read as its collider, and it never was.

## Creative or Survival

The host picks **World: Creative or Survival** in the lobby room's Match section, with Sandbox chosen (Single player has a
Survival tick box). Clients see the choice and can't change it. It is kept in `settings.txt` (`sandbox_survival`), defaults to
Creative, and takes effect at the next match start, which clears hands, inventories and constructs as every match start does.
Code: `Source/Game/GameSandboxSurvival.cs`.

- **One answer.** `Game.SandboxInfiniteParts` is the option and the map's `infiniteParts` together, the same on every peer:
  noclip, the look line, the parts menu, the HUD and the physics gun all ask it, and `SandboxShareRules` puts it on the hands
  and the world. **A map with `"infiniteParts": false` stays survival** whatever the option says (the lobby shows it greyed out).
- **Survival from the lobby turns the parts menu off too** (`SandboxPartsMenu`), whatever the map's `partsMenu` says: the menu's
  blueprints aren't counted, so it would make constructs from nothing. So it plays as `infiniteParts` and `partsMenu` both off:
  physical storage, salvage into the hands. `"power"` stays the map's: machines need power in survival either way.
- **Network (protocol 127).** `Msg.Welcome` ends with a survival byte, so a client predicts correctly from its first tick, and bit
  4 of `Msg.LobbyState`'s second Zombies byte carries changes, read on every copy like the gravity. A client no longer learns the
  rule from its inventory flag.
- **The start kit.** From an empty start nothing can make a Drill Module, a Saw Module or any machine, so a survival game
  chosen in the lobby starts with `Constructs/SurvivalStart.json`: Starter Assembler, Smelter, Foundry and Sawmill (each a machine,
  its bins on chutes and a Small Genset on a Fuel Tank, anchored on 4 m slabs), a Drill Rig and a Saw Rig. Each goes on the nearest
  clear, level ground round the rack (`SandboxKitPiece`; no room: it is left out and logged). They belong to nobody, as the map's
  constructs do. A rig starts frozen, upright, and is under the survival physics gun's 500 kg while its bin is near empty: carry
  it to a vein, hold the bit just over the ground, freeze it. A map's `"survivalStart": ["name", ...]` (blueprints or parts)
  replaces the list and also applies to a map that is survival by itself; `[]` gives nothing. Without it such a map gets no kit.
- **What a fresh game can reach** (choices made, the developer may change them): gather iron, stone, sand and wood; smelt, saw and
  found metal, glass, planks, cloth, steel, concrete, rubber and laminate; a tier 1 Assembler makes 312 parts, its own machines and
  more Fuel Tanks (fuel) among them. **Tier 2 is closed**: copper needs a tier 2 drill and smelter, those need Wiring Looms, and
  Looms need copper and a tier 2 Assembler. So Batteries, Engines, Alternators and Gensets come only from salvage. The rack hands out
  only what the abstract inventory holds, and survival salvage goes to the hands, so it stays empty. A map under 150 m half width has no
  deposits unless it places some (the Construct Yard): the lobby says so; the ground drill still gives stone and sand there.

## Salvage and the inventory (milestone 3)

Decision 7, the loot loop: what you salvage is what survives, so disabling an enemy by shooting its cheap exposed parts
leaves more to take. Board item dev-sandbox-m3, part 2 of 4 (the developer approved it 2026-10-29).

- **The inventory.** Every player has one on the server (`SandboxInventory`, Source/Constructs/ConstructInventory.cs):
  a count of each part kind (by part id, up to 9,999 each), the **materials** they carry (loads taken from bins; up to 9,999 each; scrap is gone, see [Salvage haul](#salvage-haul-decision-31))
  (resources come later; always empty now, but the wire carries them). Each player's client hears its own whole
  inventory when it changes (protocol 81), so the HUD and the inventory window show it; nobody else's is sent. It resets
  with the match like the constructs (nothing is saved yet). It survives death; a leaving player's empties (their id may
  be someone else's next). The read API: `Game.SandboxInventory(playerId)` (counts in `Parts[partId]` or `Count(id)`,
  `Materials`, `TotalMaterials`, `TotalParts`, `Kinds`, `Infinite`, and `Version`, which goes up on every change so a window can
  rebuild its text only then). On a client only the local player's is known.
- **Owners.** A construct a player starts (sets its first part down, or spawns from the parts menu) is **theirs**, and
  what is built onto it stays theirs; the map's constructs are nobody's. A piece that falls away keeps its owner. A
  leaving player's constructs become nobody's.
- **Salvage.** With the tool the part needs (wrench or welder) out, hold aim on a part: the same timed removal as taking
  a part off (60% of its fitting time), but it goes **into the inventory**, not the hand. Anyone may salvage a **wreck**
  (a piece cut off, or a broken-off part), a **loose part**, a construct **nobody owns**, or someone else's construct
  once it is **disabled** (any part of it wrecked: broken off or stopped working; `Construct.Damaged`). Someone else's
  working construct is refused: "that construct is someone else's: disable it first (shoot a part off)". Nobody may take
  parts off while someone drives it. The look line says "hold aim to salvage it" (or "to take it off as a wrecked part", or "someone else's:
  disable it to salvage").
- **Wrecked parts (not scrap).** A part that was destroyed (a wrecked part: broken off as debris, or stopped working in
  place) is **not** salvaged on site any more (decision 31, [Salvage haul](#salvage-haul-decision-31)): taking it off leaves
  a physical wrecked part, and a lone wrecked part can't be salvaged again.
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
  material") whenever the inventory holds anything.
- **Debris expiry.** A **wreck** (a piece cut off, or a broken-off part) is cleared away once nobody has been within
  `debrisNear` metres of it (12 by default; or its own size) and nobody has been taking a part off it for `debrisSeconds`
  (180 by default; 0: never), checked every half second. Constructs players build and loose parts they set down never
  expire. The cap on bodies (64 in all) still stops new debris appearing when the world is full.

Choices made (the developer may change them): salvage always goes into the inventory, even with infinite parts on (so
the loot loop and the counts work on the yard); a construct counts as **disabled** as soon as any of its parts is
wrecked (there is no repair yet, so it stays salvageable); after a fit the next of the same kind comes into the hand
automatically; the debris defaults (3 minutes, 12 m); inventories survive death but not leaving or a new match; the
scrap rates by material.

## Storage volumes

A construct's own interior as storage. The **Storage Volume** (`Constructs/Parts/Storage/StorageVolume.json`, wrench, 14 kg, 400 health) is a small hopper with a box of space on its construct that you size yourself. It is one custom-sized store for **bulk or parts**: one bulk material, as a bin holds, or stored parts by kind, never both at once.

- **Why a part of its own, not a mode of the Cargo Volume.** A cargo volume is a logic part: its signal freezes bodies and it has no store. Everything a bin does (the store, chutes, F, the look line, spilling, mass) hangs off `PartRole.Storage` and `CPart.Store`, so a storage part with the cargo volume's six box settings gets all of that, and the box comes along with the sizing tool for nothing. One part with two modes would have needed a store on a logic part and a per-part switch at every one of those places.
- **Sizing: the cargo volume's own tool.** With the configure tool out, every box near you shows a handle on each face: hold Fire on one and drag (10 cm grid, the opposite face stays); R on the hopper then Fire-drag draws a new box corner to corner; Fire on the hopper opens the panel, where size and centre are numbers (`sizeX/Y/Z` 0.2 to 40 m, `atX/Y/Z` up to 40 m from the part). While you drag, the look line says what that box would hold. A big box is set from wherever its face is: the server measures your reach to the box, not to the part. Handles only show within the tool's 8 m, so walk to a far face.
- **What counts: the whole box as you size it** (`ConstructStoreVolume.Usable`; the developer, 2026-11-02). A box isn't clipped to the construct: it holds its full volume even with no walls round it, and its fill draws to that size. The box and fill don't collide, so walls are the player's choice.
- **Capacity.** Bulk: `binUnitsPerCubicMetre` in `Constructs/Recycling.json` (400 a cubic metre, so 60 m3 is 24,000 units, and iron ore is 1.6 t a cubic metre), at most 10,000,000 units a box. Parts: each takes its bounding box (at least a litre) out of the usable box times `itemPackShare` (0.7: the gaps), at most 256 kinds and 60,000 of a kind. Every part that is an item goes in, however many: physics-only parts are refused as for any storage (a cargo volume carries them), and so is storage itself.
- **Not solid.** The box and its fill collide with nothing: you walk through, bodies pass, and a shot passes through the fill. Only a player's look ray meets the fill (`Raycast(..., look: true)`), so F works at the level wherever you stand, and a stray round can't land on a hopper 30 m away. Reach is to the box. The walls are your panels.
- **Its fill** rises along the part's up from the usable box's floor: the material's colour, or a crate colour for parts, a box with three lumps on top, four draws at any size. Parts are not drawn one by one: a level and the look line's count stand for them ("Storage Volume: 34 parts, 3 kinds, 62% full: Lamp x10, Engine Mount x2... - F: take a Lamp"). Bulk: "Storage Volume: 1200/24000 iron ore - F: take 100".
- **Using it.** F tips in the materials you carry, or takes a load of 100; with parts in it F takes one of the kind put in last into your hands (the same rules as a slot: full hands go to the backpack, or it's refused). Holding a part, a click at the hopper or the fill puts it in, and the look line says why when it won't go.
- **Chutes.** Two named ports on top of the hopper: **Chute in** (green) and **Chute out** (orange), one way each, both in the linking tool's picker. In a blueprint a chute names them with `"toPort": "Chute in"`, `"fromPort": "Chute out"`; a chute that names none takes the only port that way. A chute moves a load of 160 bulk (the Storage Volume's `chuteLoad`, like a Plasma machine) or one part a second (`ConstructRoutes.ChuteMove`): parts come from slots or another volume, and go on into slots or a volume, and what won't go stays at the source. Named chute ports work on any storage part: a part with `"type": "chute"` ports has them instead of its plain one.
- **Mass.** A load counts at the fill's centre through the usual path (`Construct.LoadOf`, `LoadStep` at 4 Hz, "Bin loads"); stored parts weigh their own mass, unlike parts in slots. `LoadKey` is a double, so one unit off a huge load still shows. 10,000,000 units of gold ore is 80,000 t: a buggy's wheels bottom out and it stays put, with every number finite.
- **Limits.** 4 Storage Volumes a construct. A box may not grow into another on its segment ("that box runs into another Storage Volume"; a fresh part's default box may start overlapping one, and only growing the overlap is refused). A box too small for what it holds is refused and undone ("Empty it first").
- **When the construct changes.** The store moves with the part through a split. Capacity follows the bounds, so a split or a lost wall can leave it **over-full**: nothing is lost, the load keeps its weight, it draws full, and nothing more goes in until it is under again. The wrench refuses to take off a Storage Volume that holds anything ("empty the Storage Volume first"). The remover still deletes it (and spills). A destroyed one spills up to 24 Material Bundles, or loose parts, in the box; the rest is lost and logged.
- **Survival.** It costs what its part costs, whatever the box's size; the box itself is free. Anyone may take from it, as from any store, and the contested rule (decision 19) holds for F, a click and the configure tool.
- **Blueprints.** The box is its settings, so a step's `"config"` holds it and `ConstructConfig.ToJson` writes it; `ConstructRoutes.LinksJson` writes the ports. Contents aren't in blueprints, as for bins. The **Storage Bay** blueprint is the example.
- **Network (protocol 116).** Two more bits in a store's flags byte: 8, the amount's top u16 follows (a store under 65,536 costs what it did); 16, parts follow (u16 kinds, then u16 part and u16 count: 4 bytes a kind, 1,032 bytes at 256). `CBody` already carries settings and port bytes. Bad values are refused.
- **Performance.** The cost is a box clip per look at the store; a kinds list of at most 256, walked on a change. The arrays are made once, the first time parts go in, and stay with the pooled store. The steady path (chutes, the load step, fill and fit checks) allocates nothing.
- **Reading any store** (for things that list or fetch across stores): `ConstructWorld.Holdings(c, part, span)` writes one `CHolding(IsPart, Id, Count)` per kind held, for slots, bins and volumes, on either side; `ConstructWorld.TakeHolding(c, part, what, count)` takes up to N of it on the server and returns how many came out. Reach and the contested rule are the caller's.

Code: `Source/Constructs/ConstructStoreVolume.cs`, `ConstructCargo.cs` (the box), `Source/App/Program.SandboxCargo.cs` (the handles), `Source/Render/ConstructRenderCargo.cs`. **Self-test**: "Custom bins" and "Custom bins network" (`Source/App/SelfTestCustomBin.cs`).

Choices made (the developer may change them): a separate part, not a cargo volume mode; 400 units a cubic metre against a Materials Bin's 2,200, since loose bulk packs less tightly than a bin's rated load; 40 m and 10,000,000 units; 160 a second a chute however big the box, so filling 24,000 by one chute takes about 2.5 minutes; over-full rather than spilling the excess; the hopper is the one weak point: destroy it and nearly all of a huge load is lost; parts drawn as a level; the kind put in last comes out first by F and by chute; no pick of a kind at the unit itself.

## Salvage haul (decision 31)

Board item dev-sandbox-salvage-haul-sh01, the developer's decision 31 (2026-10-31). It replaces the on-site scrap above and is the
first piece of decision 14's refining chain.

- **No on-site scrap.** There is no scrap count on the player, no "salvage for N scrap", no Scrap Bin. Taking a wrecked
  part off a construct (the wrench or welder, the same timed edit) leaves a **wrecked part**: a single-part body that is
  darkened, damaged and carries the part's mass and material. It is a body whatever its size (a lamp or an engine); the
  physics gun, a hoist, a cargo volume or a vehicle carry it. It **can't be fitted** to anything (a wrecked part has no
  sockets to build on) and a lone wrecked part can't be salvaged again ("a wrecked part already: haul it to a Recycler").
  Debris that broke off is the same thing. Wrecked parts clear away like other debris. An **intact** part taken off is
  still a usable part (decision 7); the contested-construct rule for enemy wrecks is unchanged.
- **The Recycler** (`Constructs/Parts/Storage/Recycler.json`, welder, 220 kg: physics-only like any welder part): a steel
  hopper with a bin for its output. Leave a wrecked part in its **intake** (the box in `storage.recycler`) once nothing
  holds it (the physics gun has let go; it is not riding in a cargo volume) and it is taken in, one at a time, ten times a
  second. Intact parts and bodies with more than one part are never taken in. It processes for
  `baseSeconds + mass * secondsPerKg` (2 s + 0.04 s a kg, at most 15), then the material arrives in its bin; the part's
  **class** (its role's entry in the table's `roles`, else its `material`) and mass give the units. If its bin holds another
  material, or has no room, the part stays in the intake until it does. A **chute** (routed link) from the Recycler to a
  Materials Bin carries the output on (the Recycling Station blueprint does this).
- **Power.** In survival (infinite parts off) it needs the electrical system: a working battery whose links reach it with
  charge in it ("without power nothing happens"), and draws its `power` (600 W) only while it works. In **creative**
  (infinite parts on) it works instantly and needs no power.
- **The table** (`Constructs/Recycling.json`, format `call-of-slop-recycling/1`; `RecyclingTable`): `materials` are the bulk
  materials in id order (the wire id is 1-based: metal, wood, rubber, circuits, stone, glass, cloth, sand, the ores, dirt, then the refined copper, gold and planks), with their
  `colours` (the bin's fill); `classes` map a class to what it `gives` and `perKg`; `roles` map a part role to a class (logic,
  battery and alternator parts are electronics: circuits); the timing keys. Steel and armour give metal (0.5 and 0.6 a kg:
  a 120 kg engine, 60), wood wood, rubber rubber, concrete stone, electronics circuits (1 a kg), at least 1 a part. A bad
  file is refused with the reason and the built-in defaults are used. Material names are the later assembling step's inputs.
- **Bins hold one material at a time.** The **Materials Bin** (the old Scrap Bin, 600 units) takes any one; the first deposit
  sets it and it frees when empty (`CStore.Material`). A chute only moves like to like. F tips in the materials you carry, or
  takes a load of 100 into your carried materials (`SandboxInventory.Materials`, up to 9,999 each).
- **A load has weight.** What a bin holds counts in its construct's mass, centre of mass and inertia, at the fill's own place
  and on the segment the bin rides on, for every part with `storage.bulk`. `kgPerUnit` in the table gives each material's
  weight and `defaultKgPerUnit` any other's (metal 2, wood 1, rubber 1.5, circuits 1, stone 3, glass 2.5, cloth 0.5, sand 2,
  iron ore 4): 600 metal is 1,200 kg, and a full buggy leans to its bin and drives a third of the way in the same time. A
  weight outside 0.01 to 100, or one for an unknown material, refuses the file with the reason. See "Bin loads" below.
- **Spilling.** A broken bin spills up to six **Material Bundles**: wrecked parts of 100 units whose Setting is the material
  (`"yield"` in the part file); feed them to a Recycler to get the material back.
- **Old scrap.** Scrap counts from older inventories load as metal (one for one) in up to three Materials Bins set down
  beside the player, the rest carried (`ConstructLegacyScrap.Import`, `Game.SandboxImportLegacyScrap`). Nothing saves
  the Sandbox inventory yet, so there is no live caller; the function is what a world or inventory load calls.
- **Network (protocol 106).** The inventory message drops its scrap; a store (`CBody` Store) ends with its material (u8)
  and a flags byte (u8: 1 busy, 2 held off by its Run wire, protocol 111; 4 a machine's input follows, protocol 114). The wrecked part and its darkening are the existing `CBody` (Broken flag). The job itself (ticks
  left, units) is server-only.
- **Performance.** The Recycler step looks only at constructs with a Recycler (`Construct.HasRecycler`), counts a job's ticks
  and scans the intakes at 10 Hz; nothing allocates except the log line when a part goes in or comes out.

Code: `Source/Constructs/ConstructRecycler.cs` (the step), `ConstructRecycling.cs` (the table), `ConstructLegacyScrap.cs`,
`ConstructGive.DropWreck`, `ConstructStorage.cs` (materials in bins). **Self-test**: "Salvage haul" and "Salvage haul network"
(`Source/App/SelfTestSalvageHaul.cs`).

Choices made (the developer may change them): a wrecked part is a body at every size (the hands and backpack hold parts by
id, not wrecked state, so small wrecks are not hand-carried yet: use the physics gun or a cargo volume); the Recycler takes
a wreck from the physics gun only once let go; one part at a time; the intake is the hopper and the bin's inside; creative is
instant and unpowered; the rates above; bins take one material at a time.

## Gathering (decision 14)

The first step of the resource chain: raw material comes out of the world on its own, and it comes grouped by type (the
developer, 2026-11-02: plentiful, but a forest has a lot of trees and an iron vein is quite large).

- **The field** (`Constructs/Resources.json`, format `call-of-slop-resources/1`; `ResourceTable`, `ResourceField`,
  `Map.Resources`). Each kind scatters large deposits, one `chance` per `cell` metres square: `forest` (wood, 60 a tree),
  `iron vein` (long, thin, 60,000 iron ore), `copper vein`, the rarer `gold vein`, `stone seam` and `sand bed`. Deposits are generated from the map's name, so
  every peer holds the same field and no map file changes. A world smaller than `minWorldHalf` (150 m) generates none, so
  the Construct Yard stays bare.
- **A map's say** (`"sandbox": {"resources": {...}}`, `SandboxResources`): `"generate": false` turns generation off, `"seed"`
  picks another layout, and `"deposits": [{"kind", "x", "z", "radius", "aspect", "yaw"}]` adds its own (`aspect` and `yaw`
  are optional). An unknown kind refuses the map.
- **Trees** stand on a 6 m lattice inside a forest, 80% of the cells, never on a solid box's footprint. A tree is known by
  its lattice cell (`ResourceField.TreeKey`), so only felled trees are stored or sent. They draw through the foliage
  renderer (`DrawAround`: 800 m around the camera, at most 30,000, rebuilt after a 150 m move or a felled tree), each with
  a drawn `FoliageType.Trunk`, because map trees get their trunks from solid boxes and these have none. A felled tree
  leaves a drawn `FoliageType.Stump` (map files can place neither), and a saw passes it by.
- **Solid trees** (`ResourceTrunks.cs`, `ConstructTrunks.cs`). A trunk is an upright cylinder, 5 cm of radius per metre of
  tree, up to the drawn trunk's top (62% of the tree; the canopy isn't solid). It is not in `Map.Solid`: trees aren't map
  data, so the content hash, map bake, pins and nav graph never see them. `PhysicsWorld.Forest` (the game's live world
  only, `WithTrees`) answers rays, body, point, box, capsule and `Gather` queries through `QueryLayers.Trunks`
  (`ShapeKind.Trunk`, its index the tree key): a look at the lattice cells the query crosses, behind a coarse forest mask
  and a small table of "in a forest" answers, with no allocation and nothing that loops over the world. So bullets (they
  stop, no decal yet), grenades, placing parts, the capsule mover and ragdolls all meet trunks with no code of their own,
  and a felled tree simply stops answering. The classic mover pushes a body round a trunk (`PushOut`). Each
  trunk near a moving body meets the body's solid boxes (wheels included) as a speculative contact (`ConstructPhysics.TrunkContacts`, server only: `ConstructWorld.Resources`),
  also for parts beyond a joint. Bot navigation leaves trunks out (`QueryLayers.Nav`, the nav build's as-built copy);
  bots slide round them with the mover.
- **Regrowth.** A felled tree grows back after its kind's `regrowSeconds` in `Constructs/Resources.json` (default 1800, 0
  means never). The server counts it in `ConstructGatherer.Step` (`ResourceField.Cut` queues it); a tree waits, trying again
  every 10 s, while a player or a construct stands where the trunk would be.
- **Saw Module and Drill Module** (`Constructs/Parts/Gathering`, welder, 900 W at tier 1, five tiers: [Machine tiers](#machine-tiers);
  `storage.gather` holds `kind`, `rate` and the work box; `ConstructGatherer.cs`). The numbers here are tier 1's. The saw cuts the nearest standing tree in its work box, 12 wood a second. The drill,
  with its bit within 25 cm of the ground over an underground deposit, taps it at 10 units a second. The saw fills its own
  bin of 400; the drill has only a small buffer ([The drill's buffer and shaft](#the-drills-buffer-and-shaft)). F takes a load and a chute carries it on, as with the Recycler. A bin holds one material, so a drill stops
  over a different deposit until it is emptied. In survival they need the electrical system and draw power only while
  they work; in creative they need none (a drill may run from a drive shaft instead). The look line says what a module is doing, and an idle drill names the nearest
  deposit.
- **A drill digs a hole** (`DrillShaft`, `ConstructDrillShaft.cs`). One `DigKind.Scoop` through `Game.Dig` for every 60 units
  tapped (6 s) on a body that isn't moving, so the usual `Msg.TerrainDig` carries it to every peer and joiner and no wire
  format changed. `ConstructWorld.DrillDig` is the narrow hook: the game sets it, and with `TerrainDigLive` false (no
  terrain, Destruction off, or a test's world) the drill still taps and digs nothing. Decided, the developer may change it:
  the hole itself is the only state. Its depth is measured from the surface as built (`TerrainData.BaseGroundTop`,
  `TerrainEdits.TryBuiltTop`) and it ends 1.5 m down, after at most 6 scoops, however long the drill runs. A bit counts as
  touching within 25 cm of the ground, and also over a hole: not above the surface as built with the floor at most 2.25 m
  below (`DrillShaft.Touches`), so digging never moves the ground away. A drill lifted out stops; brought back, or a
  second drill set there, it resumes with no more ops. 1.5 m is chosen so a player who falls in mantles out: a deeper hole
  needs shelves wider than a half-metre column, which is a trap. Bounds: one scoop a gather step across the world, and
  drills stop at half of `TerrainEdits.MaxOps`, leaving blasts the rest. Each scoop is a little lower and narrower than the
  last, so the holes leave no overhang on open ground. An anchored construct never moves, so one over a hole stays where it
  is, hanging; a free one wakes and settles in (`Game.AfterDig`), and a loose drill keeps digging where it comes to rest.
- **Outcrops** (`ResourceOutcrops.cs`, `"outcrop"` in `Resources.json`: `tint`, `spacing`, `size`). Each underground deposit
  shows a long streak of rock clusters down its long axis, 3 to 6 rocks a cluster, iron ore rust, copper orange, gold yellow, stone grey, sand pale.
  They are worked out from the seed like the forest lattice, so they are the same on every peer and nothing is sent. Drawn
  through the foliage renderer as `FoliageType.Outcrop` (at most 16,000, on top of the trees' budget, in the same rebuild),
  never under a solid box's lattice cell, never where a stronger deposit is what a drill would find, and a map file can't
  place them (`Foliage.IsGenerated`). They aren't solid, stay when a deposit runs out (only the server knows what is
  left), don't follow dug ground until the list is next rebuilt, and like all foliage don't draw at foliage quality Off.
- **Network (protocol 111).** `Msg.ResourceFell` (u16 count, u32 tree keys) goes out as trees fall and to each joiner, and
  `Msg.ResourceGrew` as they grow back, so a joiner hears only what is felled now. How much a deposit holds stays on the
  server. A new match puts trees, deposits and regrowth timers back (`Map.RestoreProps`); nothing is saved with a world yet.
- **Performance.** The step runs at 10 Hz and looks only at constructs with a module (`Construct.HasGatherer`). Drilling
  allocates nothing; a part-cut tree takes one dictionary entry, a felled tree one more. A collision question costs a mask
  lookup per lattice cell outside a forest and a hash inside one.
- **The Run input.** Saw and Drill Modules, the Recycler and production machines have one data input, **Run** (`DataIn`, port byte 255, listed in
  the linking tool's picker; a blueprint wires it with `"links"` or `"wires"`). Nothing wired: they run as before. Wired: only
  while the signal into Run is on (`ConstructGatherer.HeldOff`, at least 0.5, adding the wires as an engine's run port does).
  A cut cable still counts as wired and carries nothing, so it holds the module off until it is mended; a deleted link is
  unwired again. It reads the part's own input, so it works on an anchored base with no seat. Held off, a module gathers
  nothing and draws nothing; a Recycler takes nothing in, and a job in hand waits without drawing, then finishes. The look
  line ends "held off: its Run wire is off". The flag is on the wire: bit 2 of a store's busy byte (protocol 112).

### The drill's buffer and shaft

The developer's request (2026-11-03): drills are more modular. A Drill Module is a compact rig (`machine_tiers.py`, `DRILL`): a 0.5 m housing with a
small hopper, a boom and a tower with a gearhead, its bit at the same place under the part as before, 200 kg at tier 1 (it was 320). It stays in the
same sockets, and side and back sockets stand 0.25 m from its middle, on pads at tiers 2, 3 and 5. Saw Modules are as they were.

- **The buffer.** 5% of the old bin at every tier: 20, 40, 70, 160 and 320 units, under 2 s of its rate. Full, it stops, draws nothing and takes no torque,
  and the look line says what to do: "buffer full: connect its Chute out to a bin, a Storage Volume or a machine", "its chute is cut", or "its chute has
  nowhere to put this" when the chute's end is full. F still takes a load.
- **Chute out.** A named chute port, out only, on the hopper's +x side (`"ports"`; a part that names only a chute output takes no chute in, by a link or a
  hose: `ConstructRoutes.IsInput`, `IsWorldInput`). It goes to anything that takes a chute: a Materials Bin, a Storage Volume, a machine's input, and through a
  hose to another construct. Its load is the tier's `chuteLoad` (10 to 160 a move, one move a second), so a chute keeps up with its drill at every tier.
- **Drive in.** A named shaft input on the gearhead (socket `drive`), facing up. Two ways to run: electric power as before, or a drive shaft. A shaft link ends
  there, or a drive part (a gearbox, a clutch, an engine's Drive out) bolts to that socket. It may come from an engine directly, with no transmission (the
  engine then needs nothing else: `Construct.HasPowertrain` holds for an engine with a drill on it), or anywhere on the drive line past the gearbox, through
  transfer parts, a power take-off's outlet or a differential's output (`Construct.BuildShaftFeeds`, `FeedShafts`: the reductions, what is left after each part's
  losses, the clutches' gates). A shaft can't cross a joint or leave the construct, as ever.
- **The rule (decision; the developer may change it).** With a shaft linked, the shaft alone runs the drill: it draws no electric power and needs no battery, and a shaft that
  isn't turning stops it with a reason, even with a charged battery there. "Linked" is the part's own link: a cut shaft is still linked, as a cut Run cable is. So a player chooses by what they connect, and nothing
  switches silently. Delete the shaft and it is on electric power again. In creative the rule is the same: creative waives electric power, not a shaft the player linked. The look line: "stopped: the
  engine on its drive shaft isn't running", "the clutch on its drive shaft is out", "its drive shaft is cut", "its drive shaft doesn't reach a working engine". The **Run** input holds it either way.
- **The rate.** Rated speed and load per tier (`storage.gather.ratedRpm`, `shaftTorque`; `DRILL_SHAFT` in the generator). The rate is the tier's rate times shaft rpm over rated rpm (at most 1), times the
  share of the load the engine can carry (`Construct.ShaftShare`).

| Tier | Rated rpm | Shaft torque | Buffer | Chute load a second |
| --- | --- | --- | --- | --- |
| 1 Scrap | 1,800 | 40 N m | 20 | 10 |
| 2 Workshop | 1,600 | 80 N m | 40 | 20 |
| 3 Industrial | 1,400 | 160 N m | 70 | 40 |
| 4 Military | 1,200 | 320 N m | 160 | 80 |
| 5 Plasma | 1,000 | 640 N m | 320 | 160 |

- **The load.** A working drill takes its torque off the engine, through its reductions and losses (`ConstructDrillDrive.Load`, at 10 Hz; `Carry`, each tick), never more than the engine gives at that
  rpm and throttle (a shut throttle still gives a quarter of its torque, `IdleTorque`). The wheels get what is left, fuel (or charge) goes by the load as well as the throttle, and an engine that can't carry
  it is pulled down and the drill slows: the look line says "overloaded: a lower gear or a stronger engine". A Plasma drill straight on a V8 is overloaded; through a Transmission's third gear (2.1:1) it isn't.
- **An engine runs for a driver or a signal** as ever, so on a base wire a switch into its `run` port and a lever into `throttle`. A lever gives idle or full throttle: a number from the logic sets speeds between.
- **Network (protocol 117).** Bit 64 of a store's flags byte: a shaft-driven drill's state follows (u8 `ShaftState`, u16 rpm rounded to 10, u8 share of its rate), so the look line reads the same on every peer. The drive line itself isn't sent: each peer works
  it out in `Rebuild`, and only the server runs the step.
- **Performance.** Part of the 10 Hz step: a drill looks through its construct's links (a few hundred entries at most) and allocates nothing. The list of feeds is made only for a construct with a drill on a drive line. With no drill on the line the engine's numbers are as before.

Code: `Source/Constructs/ConstructDrillDrive.cs`. **Self-test**: "Gathering" and "Gathering network" (`Source/App/SelfTestDrillDrive.cs`), "Link auto type" (the linking tool's whole chain into Drive in and out of Chute out).

Choices made (the developer may change them): the numbers above; the drill keeps a Chute out rather than leaving the module with no store at all; the buffer has no chute in; two drills on one engine share it by the sum of their loads; a drill turns the same either way (a reverse gear doesn't stop it); a sleeping free body with a running engine keeps the last rpm it had. **Saw Modules** could take the same split: `GatherRpm` and the rate are not drill-only in the step, so it is a data change plus a look line, but a saw's bin is what makes a tree a single trip's work, so it is a design choice, not done here.

### Bin loads

A bin's load is read from its store (`Construct.LoadOf`, through `StorePool`), never kept a second time, so every rebuild is
right after a split, a weld, a spill or a part taken off. A deposit or a withdrawal, the two places a server's bulk changes,
only raises `ConstructWorld.LoadChanged`. `ConstructWorld.LoadStep`, at most four times a second (`LoadEvery`), compares each
body that has a bin (`HasBin`) with its last build (`LoadKey`, weighted by part, so a load moved between two bins of one body
counts as a change) and redoes only the mass properties (`Construct.RefreshMass`: the same `BuildMass` and `BuildSegMass` a
rebuild uses, no drive line, links or power). Physics reads `Mass`, `InvMass`, `Com` and the inertia every step, so the
wheels, the engine's load and tipping all follow; the pose stays and the velocity stays with the centre of mass. The server
wakes a free body it changed, so a parked truck settles under its load. Clients run the same step from the stores they hear
(their mass shows in the physics gun's refusal and the look line, their centre of mass in the culling), and nothing about
mass goes on the wire. An empty bin changes no number, so the seeded digests stand. A carried body's load reaches its
carrier through the cargo step. Blueprints hold no bin contents and nothing saves a world yet, so a load isn't saved.
Self-test: "Bin loads", "Module Run input" and "Modules network" (`Source/App/SelfTestModules.cs`).

Not yet (choices made, the developer may change them): a stump isn't solid and a tree doesn't die or take damage from a crash;
a trunk takes no bullet decal; a drawn tree over dug ground keeps its height until the next redraw (its collider follows the ground); bots don't plan
round trees; a drill's hole is one depth, with no way to sink a deeper shaft; parts stored in slots add no mass (only a bin's bulk does).
The "Gathering" and "Gathering network" self-test sections cover the field, both modules, holes, outcrops and the wire
(`Source/App/SelfTestGathering.cs`, `SelfTestGatheringDrill.cs`); "Terrain digging" climbs out of a drill's hole with both movers.

By hand, the ground drill digs the ground itself into carried dirt, stone and sand (`dirt` is a bulk material in
`Constructs/Recycling.json`) and puts it back: [Sculpting](TERRAIN-DIGGING.md#sculpting).

## Refining (decision 14)

The step after gathering: raw material goes into production machines, on a base or a big vehicle, and refined material comes
out. Chutes move it between them. [Assembling](#assembling) uses the results. The chain has three steps: raw ore and wood; first tier
materials (a Smelter's metal, copper, gold and glass, a Sawmill's planks and cloth, with stone and sand as they come out of the ground); second tier materials
from a **Foundry**, which mixes two first tier ones.

- **The machines** (`Constructs/Parts/Refining`, welder, physics-only; five tiers each, the numbers here are tier 1's, and what each looks like is tier 2's, the plainest: each tier has its own shape, [Machine tiers](#machine-tiers)). All are buildings. The **Smelter** (3.5 m, 5,145 kg, 3 kW): a brick furnace with
  a chimney, a glowing mouth, a hopper on top (its input) and, across an aisle, a tray at the front (its output). The **Sawmill** (3 m, 2,160 kg, 1.8 kW): a blade
  between a trough at the back (input) and a rack at the front (output). The **Foundry** (4 m, 7,680 kg, 3.5 kW): a double furnace with a hopper across its top and twin stacks, with a mixed input. Any part with `storage.refine` is a machine: its
  `machine` names it in the table, `capacity`, `min` and `max` are the input bin, and `storage.bulk` (material `any`) is the
  output bin. Both bins draw their fill, in the material's colour (all bins now do), and both must hold at least a batch (100).
- **The table** (`Constructs/Refining.json`, format `call-of-slop-refining/1`; `RefiningTable`). Each recipe gives `machine`,
  `input`, `unitsIn`, `output`, `unitsOut`, `seconds` and optionally `minTier`. A machine has one recipe per input material: what is in the input
  picks it. Units are 1 to 100, seconds 0.1 to 600 (at speed 1; work left over from a step carries into the next batch), materials are the names in
  `Recycling.json`. A bad file is refused with the reason (`--check-construct-parts` prints it, `RefiningTable.Problem`) and
  the built-in recipes are used. A part naming a machine with no recipes is refused.

| Machine | In | Out | Seconds a batch | Rate |
| --- | --- | --- | --- | --- |
| Smelter | 2 iron ore | 1 metal | 0.5 | 2 metal/s |
| Smelter | 2 copper ore | 1 copper | 0.6 | 1.7 copper/s |
| Smelter | 4 gold ore | 1 gold | 1.0 | 1 gold/s |
| Smelter | 2 sand | 1 glass | 0.5 | 2 glass/s |
| Sawmill | 1 wood | 2 planks | 0.5 | 4 planks/s |
| Sawmill | 3 planks | 1 cloth | 1.0 | 1 cloth/s |

  Iron ore gives the same `metal` as the Recycler. A Drill Module (10 a second) feeds several Smelters (4 ore a second each). A
  chute moves 10 a second, more with a higher tier machine at either end.
- **The Foundry's recipes** (machine `foundry`). A recipe lists `inputs`, 2 to 4 of `{"material", "units"}`, in place of `input` and `unitsIn`, and all of a machine's recipes
  are one kind or the other: a mixed machine's `RefineMix` makes its input `CStore`'s mixed one. Same material twice, a repeat of the same set, or the two kinds on one machine are refused.

| Second tier | In | Out | Seconds | From tier | Chains |
| --- | --- | --- | --- | --- | --- |
| steel | 2 metal + 1 stone | 2 | 1.0 | 1 | iron, stone |
| concrete | 2 stone + 2 sand | 3 | 0.8 | 1 | stone, sand |
| laminate | 2 planks + 1 glass | 2 | 1.0 | 1 | wood, sand |
| rubber | 2 planks + 1 sand | 1 | 1.0 | 1 | wood, sand |
| bronze | 2 copper + 1 metal | 3 | 1.0 | 2 | copper, iron |
| circuits | 2 copper + 1 gold | 2 | 2.0 | 3 | copper, gold |

- **A mixed input.** Each material keeps its own amount, up to the input's capacity (`CStore.MixMat`, `MixAmount`, at most 8 kinds). A material goes in only if some recipe of the machine's tier uses it and everything already there
  (`ConstructRefining.MixTakes`), so what is in the input picks the recipe: metal alone waits for stone or copper, metal and stone makes steel, and sand then stays in its source ("no recipe mixes it with 3 metal"). Run one chute in for
  each input: a port takes several. A pair runs until one side runs out; what is left of the other waits for its fellow.
- **Materials.** `copper`, `gold` and `planks` are ids 13 to 15, and `steel`, `bronze`, `concrete` and `laminate` 16 to 19 (2.5, 3, 3 and 1 kg a unit; `rubber`, `circuits` and `cloth` were salvage only, and now have recipes). The ceiling is 63 materials
  (`RecyclingTable.MaxMaterials`; the inventory has 64 slots): a material id is a byte on the wire, so only arrays grow. A Sawmill keeps its weight (1 wood to 2 planks); a Smelter's slag is lost.
- **Two bins in one store.** `CStore.Amount` and `Material` are the output, so a chute out, F, a spill and the mass treat a
  machine as any bin, and a machine's own `InAmount` and `InMaterial` are the input. Only `ConstructWorld.Feed` adds to the input,
  and only the step takes from it (when a batch is done, so a batch in hand loses nothing if the power goes or the machine breaks).
  A chute into a machine ends at `Feed`, a chute out starts at the output: there are no separate chute ports.
- **F** tips what you carry into the input, if it takes it and has room, else takes a load of 100 from the output; the output is
  never tipped into and the input never taken from.
- **Refused and blocked.** A chute bringing what the machine has no recipe for, or another material than its input holds, moves
  nothing: it stays in the source, and the look line says so for 4 s after the chute last tried (`CStore.Refused`). A
  partial batch waits for more. An output that holds another material or has no room for a batch stops the machine until a chute or F
  empties it. The look line: "Smelter: in 40/200 iron ore, out 12/400 metal - REFINING iron ore to metal - F: take 12 metal".
  All but the refusal are worked out from the store on either side (`ConstructRefining.StateOf`, `Status`, `Look`).
- **Power and Run.** As the Recycler (`ConstructRecycler.Powered`): in survival it needs a working battery whose links reach it,
  with charge, and draws only while it works. A wire into **Run** holds it off (`ConstructGatherer.HeldOff`): it waits, keeping the
  batch's progress, and draws nothing. The machine reads its own input and power, so it works on an anchored base with no seat.
- **Creative** (`CreativeRecycling`, infinite parts on), as the Recycler: no power, and instant: each step refines all it can (as
  many batches as the input holds and the output has room for), never busy. The Run wire still holds it, and a recipe's input still
  counts: a wrong material is refused as ever. A logic part wired to Run has no power on a flat battery either, so it holds the machine off.
- **Mass.** Both bins count (`Construct.LoadOf`: one load per part, the two weights added at their shared centre, in a box round
  both), by the usual `LoadChanged` path. `LoadKey` also follows where a load sits, so a batch that keeps its weight still moves
  the centre of mass.
- **Spilling.** A broken machine spills up to six Material Bundles for each bin, of that bin's material.
- **The Smelting Station** blueprint: a Smelter on a Concrete Foundation 4x4 m, and a second slab beside it with a Battery and two Materials Bins, chutes from the first bin into the
  Smelter and from the Smelter to the second. Tip ore or sand into the first bin (or chute it in), take the product from the second.
  A battery of 300 kJ runs a Smelter for 100 s of work: 200 metal. The **Foundry Works** blueprint is the same with a Foundry and two bins feeding it: tip metal into one and stone into the other.
- **Chute ports.** The machines have named ports on their outside, `Chute in` and `Chute out`, both on the +x side, so a chute needn't reach a building's middle. A port takes any number of chutes.
- **Network (protocol 114; 117 for the mixed input).** Bit 4 of a store's flags byte: a machine's input follows (u16 amount, u8 material, u8 refused
  material), only when it holds something or a chute is refused, so other stores cost the same 5 bytes (plus their slots). Bit 64: a mixed input follows (u8 kinds, then u8 material and u16 amount). The job's
  progress is server-only.
- **Performance.** The step (`ConstructRefining.Step`) runs at 10 Hz and looks only at constructs with a machine
  (`Construct.HasRefiner`). A recipe is an array lookup behind a dictionary; nothing allocates.

Code: `Source/Constructs/ConstructRefining.cs` (the step, the look line), `ConstructRefiningTable.cs` (the table), `ConstructStorage.cs`
(`Feed`, the fill box, the bytes). **Self-test**: "Refining" and "Refining network" (`Source/App/SelfTestRefining.cs`).

Choices made (the developer may change them): the numbers above; the input can't be taken back out by hand (run it through, or break the
machine); a machine takes one input material at a time, so a stray unit short of a batch waits for its fellows; a refusal shows on the
machine's look line, not the source bin's; no named chute ports; pipes carry no bulk yet, only chutes do; the Sawmill has no blueprint of
its own.

## Extrusion

The developer's mothership loop: a machine that makes long pieces to length, for building panels, frames and shapes. An
**Extruder** pushes bulk material through a die; what comes out is a real, parametric piece; a **Cut-off Saw** across its
path cuts it off. Code: `Source/Constructs/ConstructExtrusion.cs` (the pieces), `ConstructExtruder.cs` (the machine and the saw),
`ConstructExtrusionTable.cs` (`Constructs/Extrusion.json`). Self-test: "Extruder", "Extruder network" (`Source/App/SelfTestExtruder.cs`).

- **A piece** is a cross-section swept along a length: an 8 by 8 cell grid (a u64: bit y * 8 + x), a width and height (0.1 to 4 m),
  a length (0.05 to 32 m, in 5 cm steps, so both the 0.1 m and 0.25 m lattices can be hit) and a material. That is all it is
  (`ExtrusionKey`). The grid is meshed into at most 16 solid boxes, the same way on every machine; a solid bar is one box, an
  I-beam three. Presets: Solid bar, Plate, Wall (a skin each side on three studs), Hollow box, I-beam, C-channel, L-angle, T-bar, Cross;
  Custom is the panel's own grid. A section must be connected and mesh into 16 boxes; each refusal says why.
- **A piece is a real part.** `Extrusion.Def(key)` makes a `ConstructPartDef` the first time a key is asked for and keeps it: ids from
  `Extrusion.RuntimeBase` (0x4000), resolved by `ConstructParts.Def`, never sent. So `Rebuild`, split, join, the renderer, rays, player
  blocking, ghosts, welds, damage and paint read a piece's boxes, sockets, mass and health as they read any part's, with no second path.
  `ConstructParts.All` doesn't list them, so menus, racks and part sweeps see only the files. Bounds are the real section's; collision
  and hits are its solid boxes through `Construct.SolidCount/SolidBox`.
- **Numbers.** `Constructs/Extrusion.json` by material: `unitsPerM3` (mass is that at the material's `kgPerUnit`, so a piece weighs what
  went in), `healthPerM3`, `armourPerCm` of its thinnest wall up to `maxArmour`, and `class` (salvage, hits, burning). A row may name a
  material Recycling.json doesn't have yet (steel, concrete): it is simply not usable until it does. Metal: 200 units/m3, 400 kg/m3.
- **Sockets.** A finished piece has `base` and `tip`, and four faces (`+x0`, `-x0`, `+y0`, `-y0`...) at up to nine stations about 1 m
  apart (further on a long piece), 38 at most, each on the outermost cell of that side nearest the middle. Point and freeform welds reach everywhere else. A
  piece is physics-only ("extruded"): welder, physics gun, cargo volumes.
- **The Extruder** (`Constructs/Parts/Extrusion/Extruder.json`, `storage.extrude`): a bin (`any`, 400) with a die at its +z face, 0.2 m/s, 900 W
  while it runs. Material comes by chute or F; Run and power as a [production machine](#refining) (`ConstructGatherer.HeldOff`,
  `ConstructRecycler.Powered`; creative needs no power but still needs material). Its output wire is the length so far, in metres.
  Configure tool: Profile, Width, Height, and the cell grid (four `mask` settings drawn as one grid: click a cell and what is shown
  becomes Custom). A change of setting is for the next piece; the panel previews the section on the die.
- **Die Frames** (`"dieFrame": true`; Die Frame 0.5 x 1 m, Large Die Frame 1 x 2 m). The machine starts with a 0.5 by 0.5 m die. Weld frames to it (and
  to each other) on any side: the die reaches across every working frame joined to it, up to 4 by 4 m, and its centre moves with
  them (`ConstructExtruder.Die`). The section is held to the die, and the look line says to add frames.
- **Growing.** The piece is a part of the machine's own construct, linked to its `die` socket (kind `die`: nothing else fits), its origin
  at its base end, so it rides with the machine (a mothership included), counts in its mass and is solid. Everything is found from that link
  (`ProductOf`), so nothing has to be kept in step through renumbering, splits or the wire. At 10 Hz the step adds up
  growth; each 5 cm swaps the part's def for the longer one and calls `Construct.RefreshShape` (mass, bounds, contact points; no routes, power, drive line or
  guns), and sends a 5-byte `CBodyKind.Extrude`. At 0.2 m/s that is 4 swaps a second. A shape is made once, so a line in steady work allocates only the bodies it cuts off.
  A growing piece has one socket (the die's), so nothing can be fitted to it, and welds onto it are refused. A piece no longer linked
  to a die (taken off, the machine gone) finishes at the next rebuild, with its full sockets.
- **Its path.** Each step checks the new slab, box by box (an I-beam's notch stays a notch): the ground, walls, its own construct and anything
  anchored stop it ("blocked"). A free body is pushed along the path (`pushForce`, 6 kN, at most 0.6 m/s) and growth waits for it to go, so
  nothing is ever grown into; one that doesn't move in 1.5 s stops it ("too heavy to push").
- **Why it stopped** is the store's `Refused` byte (`ExtrudeStall`, on the wire): no power, needs material, can't extrude that, the bin holds another
  material than the piece, the section can't be made, at the cap, blocked, too heavy. The look line has the bin, the die, the section, the length so far, the
  material use and the reason.
- **The Cut-off Saw** (`logic.kind: saw`; Cut-off Saw 1 by 1 m, Large Cut-off Saw 4 by 4 m): a frame round a throat, across the path, on the same
  construct or another. On a rising signal into it, or (**Cut every**, in metres, no logic needed) when that much is past its blade, it cuts: what is
  past the blade comes off as a body of its own at that place, moving as the piece moved, keeping its health share and paint;
  the rest stays on the die and goes on. The cut lands on the 5 cm lattice at or behind the blade, so Cut every gives exact lengths. It needs power; its throat must take
  the section, else the piece stops on the saw's frame with a reason. Without a saw a piece grows to the cap and stops.
- **On the wire (protocol 119).** A piece in a Full's part list is a mark (0xFFFE) and its key, 10 bytes with a preset section, 18 with a custom one; ids
  are each machine's own, so `StructureHash` mixes the key's hash. A client out of table room (`Extrusion.MaxDefs`, 12,000 pieces in a run) shows a stand-in, and a server
  there stalls with "too many different pieces".
- **Blueprints.** A piece names itself: `"part": "Extrusion i-beam 25x25x400 metal"` (or 16 hex digits for a custom section) in any step or map list.
  `base`, `tip` and the face names are its sockets. **Extruder Line**: a Foundation, an Extruder, a Cut-off Saw, a Battery, a Materials Bin chuted into the hopper, and a Timer wired to the saw.

Choices made (the developer may change them): a def per length rather than per-part data (the alternative needs every reader of boxes and sockets changed, and the
table is cheap: one line is at most 640 lengths of a section); an 8 by 8 grid (a u64, a simple panel; finer needs a second word); 5 cm steps; the piece on the machine's
own construct, not a body of its own (so it uses one of the construct's 8,000 parts, and growing on a free vehicle gives no reaction); a stalled piece keeps its length; the 4 m section and 32 m length caps;
one Die Frame turned to suit rather than a wide and a tall one; a Die Frame counts in any turn; until feat/collision merges, a moving piece touches the map and fixed bodies only with its bounds corners, as every body does (something moving into a fixed piece does meet its real boxes: a post stops at an I-beam's web; the compound collider gives the moving piece the same); tiers aren't built, but `speed`, `die`, `maxSection`, `maxLength` and `pushForce` are part data, so `machine_tiers.py` can add them.

## Machine tiers

Each of the six machine lines comes in five tiers, one for each step of the threat ladder (zombies, bandits, military, the
sci-fi faction, each giving better loot): **1 Scrap, 2 Workshop, 3 Industrial, 4 Military, 5 Plasma**. Tier 1 has the plain name
(Drill Module, Saw Module, Sawmill, Smelter, Foundry, Assembler); the others are named after their tier ("Industrial Drill Module", "Plasma Smelter").

The gathering modules are vehicle-sized. The drill's bin is a small buffer ([The drill's buffer and shaft](#the-drills-buffer-and-shaft)), so the Bin column is the Saw Module's:

| Tier | Size | Solid body | Drill u/s | Saw u/s | Bin | Chute move | Power | Mass, health | Armour |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 Scrap | x1 | 1 m (drill 0.5 m) | 10 | 12 | 400 | 10 | 900 W | x1 | x1 |
| 2 Workshop | x1.25 | 1.25 m (drill 0.63 m), 1.5 m at its pads (drill 1 m) | 20 | 24 | 800 | 20 | 1,620 W | x1.5 | x1.5 |
| 3 Industrial | x1.5 | 1.5 m (drill 0.75 m, 1 m at its pads) | 40 | 48 | 1,400 | 40 | 2,880 W | x2.25 | x2 |
| 4 Military | x2 | 2 m (drill 1 m) | 80 | 96 | 3,200 | 80 | 5,040 W | x4 | x3 |
| 5 Plasma | x2.5 | 2.5 m (drill 1.25 m, 1.5 m at its pads) | 160 | 192 | 6,400 | 160 | 8,640 W | x6.25 | x4 |

Everything else is a building, large and unwieldy at every tier: a truck can't sensibly carry one, so production wants a base, a dedicated heavy carrier or (several of them) a mothership.

| Tier | Speed | Bin, input | Smelter | Sawmill | Foundry | Assembler |
| --- | --- | --- | --- | --- | --- | --- |
| 1 Scrap | x1 | 400, 200 | 3.5 m, 3.5 m tall, 5.1 t, 3 kW | 3 m, 3 m, 2.2 t, 1.8 kW | 4 m, 3.9 m, 7.7 t, 3.5 kW | 2 m, 2 m, 0.6 t, 1.5 kW |
| 2 Workshop | x2 | 800, 400 | 4.5 m, 6.3 m, 8.5 t, 5.4 kW | 4 m, 4.5 m, 3.8 t, 3.2 kW | 5 m, 6.1 m, 12 t, 6.3 kW | 2.5 m, 2.4 m, 0.9 t, 2.7 kW |
| 3 Industrial | x4 | 1,400, 700 | 6 m, 8.4 m, 15 t, 9.6 kW | 5 m, 5.6 m, 6 t, 5.8 kW | 6.5 m, 7.9 m, 20 t, 11 kW | 3.5 m, 3.9 m, 1.8 t, 4.8 kW |
| 4 Military | x8 | 3,200, 1,600 | 8 m, 6.6 m, 27 t, 17 kW | 7 m, 6.4 m, 12 t, 10 kW | 8 m, 6.6 m, 31 t, 20 kW | 4.5 m, 3.6 m, 3 t, 8.4 kW |
| 5 Plasma | x16 | 6,400, 3,200 | 10 m, 13 m, 42 t, 29 kW | 9 m, 9.9 m, 19 t, 17 kW | 10 m, 12 m, 48 t, 34 kW | 6 m, 5.8 m, 5.4 t, 14 kW |

A unit costs 90 J from a tier 1 drill and 54 J from a tier 5, 10% less a tier. A module's bin holds what its volume holds (size cubed, rounded), so
it takes about 40 s to fill at every tier. A machine's mass and health go by its size squared: 420 kg (Smelter), 240 kg (Sawmill), 480 kg (Foundry) or 150 kg (Assembler) a square metre of width, and 800, 500, 900 or 500 health.
Fitting time grows from x1 to x2.

- **A building, not a brick.** Boxes are in units of the machine's width (`BIG_WIDTH`), so every socket lands on the 0.25 m grid and a building has no pads. Each has a floor plate you step onto (2% of its width), stepped solids and
  open space: a Smelter or Foundry has an aisle across it, between the furnace and the tray, wide enough to walk through at every tier; a Sawmill is two open bays and a saw frame; an Assembler is an open frame with a low hopper at the back. Less than 55% of the bounds is solid.
- **Reach.** A part wider than 3.2 m a side (`ConstructEdits.BigPartHalf`) is reached at the point of its bounds nearest the eye, not its centre (`ConstructEdits.ReachPoint`): the wrench, welder, paint, configure tool, F and the look ray agree, and you use a 10 m machine from beside it. A smaller part keeps the old rule.
- **Spawning.** A part this size spawns clear of the player's corner distance and lifted out of the ground (`SpawnInFront`). The creative physics gun lifts up to 60 t; in survival the cap stays 500 kg, so a machine goes on by the welder or stays where it was built.

- **Data.** `"tier"` (1 to 5, default 1) in a part file, on any part; `storage.gather.rate`, `storage.refine.speed` (a multiplier on
  a recipe's `seconds`, default 1) and `storage.bulk.chuteLoad` (default 10, at least 10) carry the numbers.
  `tools/constructs/machine_tiers.py` writes all 30 files from one table per line and `tier_style.py`: fix them, not their
  output (`--check` compares). The files are `DrillModule.json`, `DrillModuleT2.json` and so on.
- **Size.** Every box, bin and work box grows by the tier's size from the part's origin, the centre of its bottom socket. Collision,
  rays, the outline, ghosts and a player's push all read those boxes, so they follow. Only what hangs below the floor keeps tier 1's
  length: the bit is thicker but still reaches 1.2 m under the part, so a tier 5 drill suits the ground clearance a tier 1 does, and a bit
  still has to be within 25 cm of the ground. A hole is the same at every tier (radius 1.4 m, depth 1.5 m); `DrillShaft`
  reads the bit's world height, so it needs no change. A saw's blade and work box grow in full, so it reaches further
  (3.1 m round the work box's centre at tier 5).
- **On the grid.** Sockets stay on the 0.25 m frame grid: scaled, then snapped. A tier 2 module's 1.25 m body falls between two grid lines, so its
  side and back sockets stand on small solid mounting pads at 0.75 m (a drill's are at 0.5, 0.5, 0.5 and 0.75 m). Bottom sockets stay at the origin, side and back ones on the
  outer faces. A bit overhangs its base at every tier, so a drill goes at a base's edge or on a
  frame. A building needs a 4 m slab at least, and a tier 3 or bigger overhangs that: its bins and battery go on further slabs, clear of it and within a chute segment (8 m) of its ports.
- **The look and the shape.** Each tier of each line has its own solid body, proportions and details, written in `tools/constructs/tier_style.py`
  (`PALETTE`, and `look(name, tier)`: `solid`, `detail`, and `hopper` when the body moves the input bin up or down); the numbers stay in `machine_tiers.py`. A line reads as itself at every tier, and a tier reads the same across lines:
  - **Scrap**: lashed up. Rust in uneven tones, mismatched plates, timber, bolted-on drums. A timber derrick, a slatted crate, a trestle mill, a squat kiln, two odd kilns, a timber frame.
  - **Workshop**: built by a craftsman. Blue paint on cast iron, cream trim, rivets. A pillar drill, a square bin with a motor, a cast mill frame with a flywheel, a brick-bodied furnace with one stack, a matched pair of towers, a machine-tool frame.
  - **Industrial**: tall and busy. Concrete grey and safety yellow, pipes, valves, hazard stripes, ladders. A stepped tower with landings, a raised cab and twin arms, a full-width gantry, a blast furnace with a stepped ramp, a stepped tower pair and pipe bridge, a bridge crane.
  - **Military**: low and hard. Olive armour on slopes, pale stencil bands, sandbags, cages, no bare pipework. A casemate with a thin mast, a hull with a wedge nose, a low blade housing, a bunker furnace, a long bunker with stub stacks, a low shelter.
  - **Plasma**: clean curves. Dark rounded shells, glowing strips and rings, a violet core. A slim pod on a neck, booms with ring blades under a capsule roof, a standing ring round the blade, a ring chamber round the core, twin capsule pods, a slim frame with a ring for the arm.

  Fixed at every tier: the rates, bins, power, mass, health, armour, widths, the bit's reach, every socket and port, the trays, and the aisle. A building's height is a range of its width (`TierHeights` in the self-test); a drill's top is always its shaft socket. A big shape is a solid box drawn as a detail shape, the box itself `"hidden"`, so the collider stays boxes. Every machine says `"collider": "boxes"`, so with the compound collider a wedge or cylinder detail stays a look.
  **"Machine tier looks"** checks that tiers of a line differ pairwise in solid shape (cells of its own size) and in colour, that each keeps the line's signature, and that details stay clear of sockets, ports, bins, trays, aisles and the assembler's open front and near the machine.
  `"glow": true` on a part box draws it unlit and bright (never paintable here). `"front"` on a shaped box (`+x`, `-y`...) says which way its front end points, so a cylinder can stand up or a wedge slope any way; the box is still the one it fills. A part takes up to 32 boxes (it was 16), each one a draw, and a box may reach 16 m from its origin
  (`ConstructParts.MaxPartReach`), for the tier 5 stacks. No part animates its work yet: only wheels, joints, dials and pistons move.
- **The gate.** A deposit or a recipe may ask for a tier: `"minTier"` in `Constructs/Resources.json` and `Constructs/Refining.json`,
  default 1. Iron, stone, sand and wood are tier 1; copper is tier 2; gold is tier 3. A drill or saw below it gathers nothing; the look
  line says "gold vein needs a tier 3 drill, this is tier 1" and still gives the nearest deposit it can work. A machine
  below it takes nothing: a chute's load stays in the source, F passes over that material, and the look line says why
  ("chute refused: copper ore stays in the source, it needs a tier 2 smelter, this is tier 1"; an empty one, "takes iron ore, sand (copper
  ore needs tier 2, gold ore needs tier 3)"). A drill taps the strongest deposit under it with anything left, so one over iron
  and gold where gold is stronger refuses, however much iron is there.
- **Rates in a step.** The 10 Hz steps add up exactly: a module's units are thousandths, a machine's work is thousandths of a
  tick, and a fast machine finishes several batches in a step. A step takes no more than the bin has room for, so nothing
  tapped is lost.
- **Chutes.** A move carries the larger `chuteLoad` of its two ends, so a chute keeps up with the machine it serves and two
  plain bins still move 10 a second.
- **The wire.** Nothing changed (protocol 114): a part's tier and the tables are data on every peer, and the refused material was already
  on the wire, so a client works out the same look line.
- **The inventory.** All 30 are in the **Production** category, sections Drills, Saws, Sawmills, Smelters, Foundries and Assemblers, in tier order, with the sub-components in Components.

Not yet (choices made, the developer may change them): tiers cost what their recipe says in survival, but nothing unlocks them, since scanning isn't built;
a full tier 5 bin weighs what it holds (6,400 iron ore is 25.6 t, gold ore 51 t) and nothing
stops a truck taking that on; a drill's hole digs faster at a higher tier but is the same size; the Recycler has no tiers. **Self-test**: `Source/App/SelfTestMachineTiers.cs`,
run from "Gathering", "Gathering network", "Refining" and "Refining network".

## Assembling

The step after refining: an **Assembler** builds parts from what the chains make. Code: `Source/Constructs/ConstructAssembling.cs`, `ConstructAssemblingTable.cs`; generator: `tools/constructs/assembling.py`.

- **The part** (`Constructs/Parts/Assembling`, welder, physics-only, five tiers: [Machine tiers](#machine-tiers)): an open gantry 2 to 6 m wide, with a low hopper at the back (its input), a tray at the front (its output) and an arm. `storage.assemble` holds `capacity` (the fill's scale), `speed`, `chuteLoad`, `batches` (how many batches of each input it buffers: 4 a tier), and `outAt`, `outDir`, where a body leaves. Its `slots` are the tray.
- **The recipe** is a setting (`"config": [{"key": "recipe"}]`): a choice filled from the table as the parts load, so the configure tool lists every recipe (a filter box stands over a long list) and a blueprint step names one (`"config": {"recipe": "Engine"}`). 0 is none.
- **The table** (`Constructs/Assembling.json`, format `call-of-slop-assembling/1`; `AssemblingTable`). Each recipe gives `output` (a part), `count` (default 1), `materials` (material: units), `parts` (part: count), `seconds` (at speed 1) and optionally `minTier`. At most 6 inputs, one recipe a part. A physics-only part can't be an input, since no chute or hands can bring one, so a chain goes through **components**. A refused file gives no recipes and the reason (`--check-construct-parts`).
- **Components** (`Constructs/Parts/Components`, items, in the parts menu's Production category): Gear Set, Bearing, Piston Set, Copper Coil, Wiring Loom, Circuit Board, Control Unit, Servo Motor, Tyre Carcass, Steel Plate, Lens.
- **Written and generated.** `tools/constructs/assembling.py` holds 46 recipes by hand (every component, and what a player reaches for first) and generates 437 for the long tail from the part files, by rule: half a part's mass in its class's material, what its role must have (an engine's Piston Sets, a gearbox's Gear Sets, an actuator's Servo Motors, a logic part's Circuit Board), and a tier from what that needs. Spawners, the Construct Core, a chest's lid and damage stages have none, nor do the weapon builder's generated parts. Fix the tool, not its output (`--check` compares).

| Example (by hand) | Takes | Seconds | From tier |
| --- | --- | --- | --- |
| Gear Set | 5 steel | 4 | 1 |
| Tyre Carcass | 4 rubber + 2 cloth | 5 | 1 |
| Wheel | 2 steel + 1 Tyre Carcass | 5 | 1 |
| Transmission | 8 steel + 2 Gear Set | 12 | 1 |
| Piston Set | 4 steel + 2 bronze | 8 | 2 |
| Wiring Loom | 2 copper + 1 rubber | 3 | 2 |
| Engine | 20 steel + 2 Piston Set + 1 Wiring Loom | 20 | 2 |
| Circuit Board | 2 circuits + 1 laminate | 4 | 3 |
| Battery | 4 copper + 2 circuits | 8 | 3 |

  A tier 1 machine asks only what a tier 1 Assembler can make, so survival can start: a Smelter is 643 steel, 322 concrete, 129 rubber and 2 Gear Sets.
- **Input.** Materials sit in the mixed input, parts in the store's kinds. Chutes bring both, as many at once as you run: a chute from a bin brings a load of a material the recipe uses; a chute from a shelf or a Storage Volume brings one part a second that the recipe wants, passing over others. What it can't use stays in the source, and the look line says so. Each input holds up to `batches` of what a batch asks. F, in order: the held part if the recipe wants it, the materials it wants from what you carry, a part off the tray, then its input back, which clears a machine whose recipe changed.
- **Output.** An item lands on the tray, in a pile of 12 a slot, and a chute, F or a beacon takes it as from any slots; nothing can be set in. A physics-only part, or one too big for a slot, leaves as a body at the output point, its near side at the point, level, and owned as the machine is. If the world or a player is in the way (or there is no room for another construct) the batch waits with its inputs, draws nothing, looks again twice a second and says why.
- **Same rules as the machines.** The batch's inputs go when it is done, so power cut or broken loses nothing. It needs power in survival (`ConstructRecycler.Powered`), draws only while it works, and takes a **Run** wire. In creative (`CreativeRecycling`) it builds a batch a step, unpowered. Its input counts in the mass (`LoadOf`), and a broken one spills its input and tray.
- **Beacons.** A network reads the tray and can take from it. It never sees the input or fills the tray (`SlotCheck.OutputOnly`).
- **The look line**: "Workshop Assembler (Engine): in 12 steel + 1 Piston Set, tray 0 - needs 20 steel (a batch: 20 steel + 2 Piston Set + 1 Wiring Loom) - F: tip in your 8 steel". Worked out from the store and the table on either side.
- **The Assembly Bay** blueprint: a Workshop Assembler set to Engine, a slab of bins, shelf, Chest and Battery beside it, and chutes to and from its ports.
- **Network (protocol 118).** The recipe is a setting; the mixed input is the Foundry's (bit 64), the parts input is a store's kinds (bit 16), and bit 128 of the flags byte adds u8 blocked and u16 refused part. The job is server-only, and a body is any other construct.
- **Performance.** `ConstructAssembling.Step` runs at 10 Hz and looks only at constructs with `HasAssembler`; a recipe is an array index; nothing allocates except the body a batch makes.

**In survival ([Creative or Survival](#creative-or-survival)).** The Assembler is, with salvage, how parts come into being: nothing comes from nothing. Tier 1 machines ask only for what a tier 1 Assembler can make, but it can't start from nothing: a first Assembler, Foundry, Smelter and Sawmill come with the start kit (or the map). A Battery needs circuits, circuits need gold, and gold needs tier 3 machines, so the first Batteries come from the map too, or from circuits the Recycler gives for wrecked electronics. In survival the physics gun stops at 500 kg, so a built machine stays where it left until the welder fits it.

**Self-test**: "Assembling" and "Assembling network" (`Source/App/SelfTestAssembling.cs`): the table and its refusals; the size table, aisle and solid share; ore to metal and stone to steel, into an assembler by real chutes with a shelf of parts, an Engine leaving as a body; missing input, blocked output, power, Run, tier gate, a refused chute; items on the tray and down a chute; F; reach beside a Plasma Smelter; creative; allocation; a real host, a client and a late joiner. "Refining" drives the Foundry.

Choices made (the developer may change them): the recipes and every number; no named input ports (one port takes every chute, and the input sorts itself); physics-only parts can't be inputs, so an Engine can't go into a Monster Truck recipe; a machine buffers 4 batches a tier; one recipe a part and one output a recipe; outputs fit in a slot or leave as bodies; a parts input isn't drawn, only a level; a batch started under one recipe restarts if the recipe changes; generated parts from the weapon builder have no recipes.

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
- **Materials Bin** (welder): an open bin of 600 units of **one bulk material at a time** (any; the first deposit sets it), its level rising in the material's colour.
- **Recycler** (welder): see [Salvage haul](#salvage-haul-decision-31).
- **Storage Volume** (wrench): a box of interior space you size yourself, up to 40 m a side, for one bulk material or stored parts: see [Storage volumes](#storage-volumes).
- **Material Bundle**: what a broken bin spills, a wrecked part of 100 units of the bin's material (the Recycler gives them back).
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
| `bulk` | Instead of slots: `material` (`any`, or one of the recycling table's materials), `capacity` (up to 60,000) and the fill box (`min`, `max`) its contents rise in. Or `{"material": "any", "volume": true}` and the six box settings in `config`: a [Storage Volume](#storage-volumes). |

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
- **A bin**: F tips in the materials you carry (the bin's own, or the first it takes; as much as fits); with none to tip, F takes a load of 100 out into your carried materials.
- **The tooltip** (the look line): "Chest: 1/2 slots used (1 part), 2 medium slots - click a lit slot to set the Lamp
  in"; "Lamp x4 (6 fit) in the Parts Shelf, 100% - F to take one"; "Materials Bin: 230/600 metal - F: tip in your 40 metal".
- **Damage**: a stored part is hit like any part (bullets, construct guns, blasts: a blast on the container hits what is
  in it); its armour counts, and at zero health it is destroyed. A container that goes (broken off, taken off, wrecked)
  **spills** what it held where it was, as loose parts anyone may pick up (a pile as many as there were, while the caps
  on bodies allow; the rest is lost and logged), and a bin's contents as up to six Material Bundles of its material. A loose container still
  holding something can't be picked up ("empty the Chest first").
- **Survival salvage**: on a map with `"infiniteParts": false` and `"partsMenu": false` (survival) nothing is counted
  abstractly: a part salvaged (or taken off your own construct) comes into your **hands**; when they are full it goes
  into the backpack if it fits there, else is set down where it came off, ready to be carried to storage. A wrecked part comes off as a wrecked-part body (hauled to a Recycler). Creative maps keep the counted inventory as before.

- **Giving a part to a player** (board item rc01): one rule, `ConstructEdits.Give` (ConstructGive.cs), for every path that
  hands a part over (taking a part off, salvage into the hands, receiver-merge pop-offs): the hands if empty, else the
  backpack (the Stow rules: a free slot it fits, at most 40 kg), else a loose pickup dropped where it was (it falls and
  rests); what the hands hold stays held, and the player gets a HUD line ("Engine dropped: hands full"). Taking from a
  storage slot or picking up a loose part with full hands goes into the backpack when it fits, else is refused (the
  part stays put); drawing from the backpack with full hands is refused. A hands state naming no loaded part is logged
  and emptied each tick, and an edit that throws is logged and dropped, never a crash.

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
loose stocked shelf can't be picked up, the bin takes 120 metal and gives 100 back with its level rising, a destroyed
chest spills its suspension and battery as loose parts and frees its store, a broken bin spills six bundles, and in
survival the gate's door salvaged comes into the hands; no allocation in 120 ticks of stocked storage, rays into the
slots, fit checks, takes and sets and the store's message; and a real host with two clients: a client's part goes in,
both clients see the contents, both grab the same part and exactly one gets it, and a late joiner sees the contents.

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
- **Survival salvage** goes to the hands (or is set down when they are full); carried materials count on the player, and bins
  store them (F tips in, or takes 100 out).
- Stored parts add **no mass** to their construct yet, and keep only the top one's health in a pile (a part taken out
  comes out whole, as the hands carry no health).
- A spilled pile becomes as many loose parts as the caps allow; a broken bin's contents become at most six Material Bundles of 100.
- A loose container holding anything can't be picked up; a construct that is cleared away (debris expiry) takes its
  contents with it.

## The Inventory Beacon

A build aid for survival, and the deliberate exception to decision 18: the developer asked for a beacon that, "when connected to
a chute network, will broadcast all the available items so the player can access them as if they were using creative mode, but in
survival", and that takes parts back to the network rather than the hands. Code: `Source/Constructs/ConstructBeacons.cs` (the table, the
networks, the views, the message), `ConstructEditsBeacon.cs` (the server's takes and returns), `Source/Game/GameSandboxBeacon.cs` (views per
player, the HUD), `Source/UI/UiSandboxBeacon.cs` (the window).

- **The part** (`Constructs/Parts/Storage/InventoryBeacon.json`, welder, physics-only, 90 kg, 120 W): a cabinet with a screen, a mast with
  three cross arms, an aerial and a lamp that lights green while it broadcasts. It is a storage part with `"storage": { "beacon": "inventory" }`
  and no room of its own: that gives it a store handle, a chute port and the machines' flags, and chutes moving items ignore it.
  It needs power in survival (`ConstructRecycler.Powered`), draws only while on, and takes a **Run** wire (`ConstructGatherer.HeldOff`). A
  wrecked one stays in place (`"onBreak": "disable"`) and says so. The **Beacon Depot** blueprint: a Foundation, a beacon, a Battery, a Chest, a
  Parts Shelf and a Materials Bin, with a chute from the beacon to each.
- **The table** (`Constructs/Beacons.json`, format `call-of-slop-beacons/1`; `BeaconTable`): `id` and `rangeMetres` (1 to 1000; the
  beacon's part to the player's eye). The shipped `inventory` beacon reaches 60 m. A bad file is refused with the reason
  (`--check-construct-parts`) and the built-in table is used. A part naming an unknown id is refused.
- **The network.** Every intact chute joins its two ends, whichever way it runs (its `FedEnd`: a chute cut anywhere before its end joins
  nothing, so a branch beyond a cut goes too), through junctions (a branch runs from the root's source) and world links. It's
  a union-find over store handles (`ConstructBeacons.Graph`). Hoses carry fluid, power and data, not items, so they join nothing.
  A Gun Workbench isn't a store here (its slots are a gun being built); a machine's output is, for taking only; its input never.
  Two beacons on one network share it; every network in reach merges; each store counts once.
- **When it is worked out.** Never per tick. `Construct.StoreNetKey` hashes a body's stores and intact chutes at each rebuild, and the
  world's loop compares one integer per body; a store freed or a world link changed does the same (`ConstructBeacons.Dirty`). The
  graph then runs at the next 10 Hz step, or at once for an action. `Stock` goes up on each change to a store, and the counts run then.
  A player's view is made at 4 Hz, only if the `Epoch`, what they reach or their state changed. All of it is fixed arrays, at most 64
  beacons (more are logged and do nothing). Nothing allocates in a steady tick.
- **Who.** A player's own beacon, or nobody's (the map's, or one whose owner left). Factions don't exist yet, so the owner is the
  faction (decision 19). Someone else's serves nobody else; a store on the network that is defended (the contested rule) is skipped.
- **Survival only.** With `"infiniteParts": true` nothing goes through a network; the look line still gives its numbers. On a counted map (the
  menu on, parts not infinite) the window is the usual one and the badges add the network's count; the inventory is used first.
- **The window.** Tab opens the same inventory when `Game.SandboxInventoryAllowed`. On a survival map it shows Parts, Materials and Tools:
  the Parts tab lists what the network holds with its counts, grouped as creative groups it; "Hide what it doesn't hold" unticked greys the rest.
  Out of range the window closes. Tab with no network says the beacon's reason, or the map's rule with no beacon near.
- **Taking** (`CActKind.Rack`, as creative's click). The server walks the real stores and takes one from the first that has it (`ConstructEdits.FromNetwork`):
  what you hold goes back first, then it comes into your hands by `Give`. A physics-only piece arrives in front of you (decision 21;
  storage takes none, so only a part that became physics-only while stored can be there). Both players asking for the last one: the first the
  server handles has it, the other hears "no Lamp left on the network". A row: after a fit or a set-down the next comes off the network (`Refill`).
  **Materials** (`CActKind.BeaconMaterial`): fills what you carry of that material up to a load (100), from the bins in turn.
- **Coming back** (`ConstructEdits.ToNetwork`): a part taken off or salvaged, what you held when you took another (the menu, the rack, the pick and
  the recent wheel), a merged receiver's pop-offs and a Gun Workbench's returns that its own slots can't hold go into a store on the network: one
  already holding that kind first, then any free slot it fits, by the slot rules. No room or no fit: the usual path, with "no room on the network
  for the Lamp: it comes to you". The HUD says where it went: "Lamp into the network (Parts Shelf)". **Physics-only pieces stay as they are**: slots
  never take them, so they are cut free as bodies. Wrecked parts stay wrecked-part bodies. Picking up a loose part or taking from a chest by hand
  stays in the hands: those are asks for the part. The remover deletes outright, so nothing comes back.
  **Materials** the player is given (the ground drill's scoops: `MaterialToNetwork`) go to pure bins holding that material, then empty ones (never a
  machine's output); what doesn't fit stays carried.
- **The HUD and look line**: "Beacon: 34 stores, 512 parts, range 60 m" (with ", 230 material" when it has some); off, the reason: "Beacon off: no
  power (a charged battery, linked to it)", "Beacon off: its Run wire is off", "Beacon off: wrecked", "Beacon off: no stores on its chute network",
  "Beacon: that one is someone else's", and, within two ranges, "Beacon: out of range (70 m away, range 60 m)". Holding a part: "12 more on the network".
- **Adding a store kind.** `INetworkStore` is all the beacons know of a store: `Serves`, `Parts`, `Tally`, `Holds`, `TakePart`, `TakeBulk`, `PutPart`,
  `PutBulk`. Add one to `ConstructBeacons.Kinds`, before `SlotStore` if both would serve the part.
- **Network (protocol 116).** `CBodyKind.Beacon`, to its player alone, only on a change: flags (1: start empty), state, stores, parts, bulk, range,
  distance, then (part id, count) for up to 240 kinds that changed and the materials that changed: about 1,100 bytes at most, in one reliable
  packet. What doesn't fit goes next tick, from where it stopped; leaving a network sends the state alone. A joiner's starts empty. A store's flags byte gains 8: a
  beacon's stores and parts follow (u16 each), for the look line on any peer. `CActKind.BeaconMaterial` (u8 material).

**Self-test**: "Inventory beacon" and "Inventory beacon network" (`Source/App/SelfTestBeacon.cs`): the table and part; the depot on the real step: chutes, a junction, a
world link, two beacons, two networks; no graph or count run and no allocation over 240 steady ticks; range, ownership, power, Run, a wreck; the real take, held part
returned, a physics-only piece, the contested last one; bulk both ways; a row refilled; a part taken off into the network, a full network into the hands, a
welded piece a body; a cut chute; the message's bound; and a real host, two clients and a late joiner, with the window headless.

Choices made (the developer may change them):

- **Delivery is instant, by radio.** It costs nothing per item. A small power draw per item taken (say 1 kJ, about a second of a Smelter) would fit the machines, and make a
  big rebuild drain a battery; it isn't in, because the beacon already draws 120 W and it would need a refusal for "battery too flat for this take".
- The range (60 m), the draw (120 W) and the 64-beacon limit. Range is measured to the eye.
- A beacon nobody owns serves anyone. Connecting a chute to someone else's store exposes it to your beacon (unless they stand defending it).
- It reads through any chute, whichever way the chute runs, though the items themselves only move one way.
- A chute cut part way drops everything beyond the cut, even if the far side still touches a junction.
- Blueprints, Spawn and Builds aren't on the survival window. Physics-only parts can't be on a network, so structure still has to be hauled.
- A material take is a top-up to 100, not 100 more. Fill still pays from what is carried, not the network.
- Bots have no view. A dead player's view empties.

## Build stations

The developer's intent: "encourage players to invest in a nice building area with connection to all their available parts". A build station is a
machine you weld to a base, power and chute to your stores; in its range it gives one building aid. Survival needs the stations; away from the
base you build by hand. Code: `Source/Constructs/BuildAids.cs` (the aids, the view, the one rule), `ConstructBeacons.cs` (states), `ConstructEditsAids.cs`
(server: who has what, parts, the gantry's reach), `ConstructMirror.cs`, `ConstructLine.cs`, `ConstructReplace.cs`, `Source/Game/GameSandboxAids.cs`
(view per player, `CActKind.BuildAid`), `Source/App/Program.SandboxAids.cs` (keys, modes, build bar), `Program.SandboxGantry.cs`, `Source/UI/UiBuildAids.cs` (the picker).

- **A station is an Inventory Beacon with an aid.** Its `storage.beacon` id names the aid, so power, Run, wreck, chute network, owner and range are the beacon's rules
  (`ConstructPartDef.Aid`), and in range it serves its network just as a beacon does: Tab opens on it, parts taken off go back to it. All stations on a build area share
  one battery circuit and chute network. `Constructs/Parts/Stations` is written by `tools/constructs/station_parts.py` (mass, power, shapes; the last box is the lamp).
  Ranges are in `Constructs/Beacons.json`. Recipes are in `tools/constructs/assembling.py` (tier 3). At most 256 beacons and stations, 64 networks.

| Station | Aid (key) | Range | Mass, power |
| --- | --- | --- | --- |
| Mirror Table | Mirror (M) | 90 m | 160 kg, 150 W |
| Line Layer | Line and fill (O) | 90 m | 180 kg, 150 W |
| Replacer | Replace (P) | 90 m | 170 kg, 150 W |
| Build Gantry | Gantry camera (U) | 120 m | 420 kg, 300 W |
| Selector | Box select | 90 m | 140 kg, 120 W |
| Blueprint Desk | Save and stamp | 90 m | 150 kg, 120 W |

- **The one rule.** `BuildAids.Free(infiniteParts)`: creative needs no stations (decision below). The server's `ConstructEdits.AidWhy` and the client's `Game.SandboxAidLive` both ask it.
- **Parts.** `ConstructEdits.TakeForAid`: creative makes them; survival takes from the inventory, then the network, then the hands. A physics-only piece (nearly all structure,
  all capital parts) is never stored or carried (decision 21), so an aid takes it from **loose pieces within 60 m**, yours or nobody's: a stock yard beside the build.
  Short: it places what it can and says how many are missing. Aiming MMB at a physics-only part with a mode on (or clicking one in the inventory) picks it for the aid; it needs the welder.
- **Mirror (M: off, side to side, front to back, up and down; Page Up / Down: move it).** The plane is the table's `mirrorAxis` and `mirrorOffset` settings (the configure tool shows them too, and they save with
  the part); in creative with no table it is the player's own. It is measured from the built-on construct's drive seat, else its root part, square to that part's axes, so one table serves every construct
  in range. A fit or a removal also happens to the image, in the same server action; a part across the plane is its own image. The image's turn is found by shape (`ConstructMirror.TurnOf`, cached):
  of the 24 turns of the part and of its Left / Right partner by name, the one whose boxes and sockets land on the reflected ones. A part with handedness and no partner takes the nearest turn and the HUD says so; a side with
  no free socket says so. The image of a stroke is not mirrored again. Freeform-welded pieces aren't mirrored, and neither are the welder's socket and point welds: only the held-part fit, the Line Layer and removal.
- **Line and fill (O).** Press on a ghost and drag: the run lies in the rest-frame plane square to the view at the press, along its two axes, so a straight drag is a row and a diagonal one a field (at most 400 cells).
  The pitch is the next matching socket along the row if there are sockets to follow, else the part's own opposite sockets, else its size. Each cell goes through `ConstructAid.FindFit` (a pose to a free socket, by the rules of a click), in order, so a cell lands on an existing socket
  or on the cell before it. Nothing is sent until the release, so a press with no drag is one part and a stroke never acts on what it made (the `SnapStroke` lesson); RMB cancels. One `BuildAidOp.Line` a stroke. The start part is stamped with its kind and place. The look line shows the run, what is short and the mass; the ghosts are made again only when the run changes.
- **Replace (P).** Aim with a part in hand (or picked) and click; with none, a click opens the picker: every part of the size class that fits, with what you have. The part keeps its index, so welds, wires and links stay;
  a link moves to the new part's matching socket, a routed link's port to the same name, else type and way. Its setting values carry over by key, its paint stays, its health stays as a share. `ConstructReplace.Plan` says why not: a connection that doesn't meet, something in the way, a store with something in it, a link with nowhere to go.
  It refuses joints that carry something, merged or builder receivers, volumes, hoses, freeform welds, couplings and world links: take those off and fit the new one. The old part goes back as a part taken off does; a physics-only one is set loose above its place.
- **Build Gantry (U).** Aim at a construct (or nearest within range): the camera leaves the body, which stands parked with its inputs off. The mouse moves a cursor, **Left Alt** and the mouse orbits, the wheel zooms up to the range, the move keys slide the point. Every tool works along the cursor's ray.
  Page Up / Down steps a build plane (0.5 m): a ray that meets nothing crosses it, and the nearest free socket to that point takes the part. The server's reach check passes anything within the gantry's range of the station (`ConstructEdits.GantryReach`), never of the body. It ends on the key, on damage, if the construct goes, or when the station fails or the player leaves its range.
- **The build bar.** Bottom centre with a tool out: one icon per aid (green edge: on here; filled: in use; red edge: a station in reach but off; grey: none near), its key, and a line in the tooltip format for what is running. A refused key says why ("Line Layer: off - no chute network - link a chute to a store"), as does the look line on a station.
- **Network.** `CActKind.BuildAid`, u8 `BuildAidOp`, then its fields. `CBodyKind.Aids`, to its player on a change: state, range and distance of each aid, the mirror plane, the gantry flag. The view is worked out at 4 Hz with the beacons' nodes; nothing allocates in a steady tick and ghosts are made again only on a change.
- **World saves.** Stations are ordinary parts and the plane is a part setting, so a save needs nothing new. A player's own creative plane and the gantry aren't saved.

Decisions (the developer may change them):

- Creative needs no stations (`BuildAids.Free`, one line). The stations can still be built there.
- A station also serves its network as an Inventory Beacon does, so a build area needs no separate beacon. The beacon stays the cheap way (60 m, no aid).
- A mirrored, line or swapped part is instant, on top of the first fit's own time: the stations are what you paid for that speed.
- Loose physics-only pieces are the aids' stock, within 60 m of the eye (`ConstructAid.StockReach`).
- Line reach is the Line Layer's range from the eye, and in creative too. 400 cells a stroke.
- The plane steps 0.125 m, the gantry's plane 0.5 m. Being hit ends the gantry; the body isn't made immune.
- Keys: M, O, P, U, Page Up / Down, Left Alt (binds version 19).

## Build stations: the Selector and the Blueprint Desk

Two of the build stations (`Constructs/Parts/Stations`; the framework, ranges and rules for a station are the stations' own: a station is a beacon whose `storage.beacon` id names its aid, and
creative needs none, `BuildAids.Free`). The **Selector** (aid `Select`) works on a group of parts of one construct; the **Blueprint Desk** (aid `Blueprint`) saves a group and stamps it elsewhere.
Every action asks `ConstructEdits.AidWhy` on the server and `Game.SandboxAidLive` on the client; the reason shows on the look line.

**The selector** is a tool like the others, held model `Constructs/Tools/SelectorTool.json`, `ConstructTool.Selector`. It has no slot on the tool wheel (the wheel's compass is muscle memory): the full stop
(`SelectTool`, rebindable) takes it out or puts it away, the Tab inventory's Tools tab lists it, and stamping brings it out. Its reach is `ConstructEdits.AidReach`, 64 m from the eye.

| Input | What it does |
| --- | --- |
| LMB | Picks the part under the crosshair. Shift: adds it, or takes it away if it is selected |
| LMB, drag | A box in the construct's frame, corner to corner: every part it touches. Shift adds; Shift starting on a selected part takes away |
| Double-click | The whole connected section: every part reached over links without crossing a joint |
| RMB, drag | A rectangle on the screen from the crosshair; the view holds still. It takes the parts whose middles are in it. Shift as above |
| RMB | Clears the selection |
| M, O, P, U | Move, copy, paint (the paint tool's colour; Reset resets), weld. The stations' switches on these keys are off while the selector is out (`Keybinds.Overlap`) |
| Delete, twice | Deletes the group |
| Comma | Saves it as a blueprint (needs a live Desk) |

**Placing** (move, copy, stamp): a ghost follows the crosshair and rests on what is aimed at, or floats at a distance (the wheel). R turns it, the arrows tip it (`GhostTurn`), LMB places, RMB or Esc stops. Aimed at a part
with a free structural socket on the construct's body it snaps there: the clip mates by its own free sockets, outermost first (K: the next one; R: the next spin). A copy or stamp stays up to place again. What lands free is **frozen
where it is** (`Construct.Frozen`, as the physics gun freezes), so a group doesn't fall as it lands; the physics gun lets it go. What lands on a socket is welded on (`ConstructPlace.FindJoin`, `ConstructWorld.Merge`). A selection in pieces
lands as a body each.

**Delete** in creative destroys the parts (containers spill). In survival they come back: through a station's chute network if it has room; else the hands, the backpack or the ground, for up to `MaxDrops` (16) parts. With more than that which can't
go on a network (physics-only, wrecked, or no network), those stay together as one body where they were, not a heap of 2,000 bodies. A refund past a full network is counted on the player.

**Paying** (survival, a paste or stamp): the ghost's line says how many parts are out of reach, and the server checks before it takes anything: the inventory, then the network (`TakeForAid`), and for a
physics-only part, which storage never holds, loose bodies of that part within reach. Short: the refusal lists what is missing, and if another player got there first what was taken goes back. A move costs nothing.

**The selection** (`PartSelection`) is a stamped list: each part with its type, place and turn, checked against the construct every frame. A part another peer renumbered is found again by its stamp; one that went drops out. The server gets it in chunks of
`MaxChunk` u16 part indices, with a hash of those parts' places and turns (never ids: extrusions differ between machines); a hash that doesn't match refuses: "that construct has changed".

**The clip** (`ConstructClip`) is a group held apart from the world: a construct in a `ConstructWorld` of its own, parts in the order a blueprint builds them, numbers in the rest frame they came from. `ConstructWorld.CopyPiece` makes one from a selection and puts one into the real world (a
design only: settings, paint, links, joints, routed links; not health, rounds or contents). A move doesn't copy: `ConstructWorld.CutOut` takes the parts out as they are, contents and all, through `SplitOff`, and what that cuts off falls away as a wreck as any removal does. The same selection gives the same clip
on server and client, so the ghost is what the server places.

**Time.** Selecting and painting are instant. The one structural edit is one tick; checks, payment, refunds, welds and a blueprint's build run as jobs of `ConstructEdits.SelectTick`, 2 ms a tick over all players. A big group's join links through the part tree (`AutoLinkMany`), so joining stays linear. Warm on a
7,921-part hull, 2,000 parts on the 2-core container under load: the request about 1 ms, the commit about 2 ms, the rebuild about 1.5 ms, `WriteFull` under 1 ms. A ghost draws its first `MaxGroupBoxes` boxes and a frame round the whole; the selection outlines its first `MaxOutlines` parts.

**Blueprints** go in the save folder's `ConstructBlueprints` folder, one file each, named after the blueprint (letters, digits, spaces and `- _ ( ) . '`, at most 32; a name in use asks). A file is a real blueprint in the shipped format, and goes by part name, so it can be dropped in `Constructs/Blueprints`. `ConstructClip.ToBlueprint` writes it: a step per part in the clip's order, on the
earliest step it hangs on, by the sockets they meet at; a freeform weld, or a piece no link joins, is a `"weld"` step. A save is built back from its own text before it is written, and must give every part. Save needs only the client's copy, so it is a client-side action; the Desk check there is the client's. All 46 shipped blueprints without cargo round-trip. Contents and merged receivers' gun parts don't go in a blueprint (the save says so).
The Tab inventory's Blueprints tab lists them above the shipped ones, with a filter, Stamp and Delete (a survival inventory shows only these). A stamp goes up to the server as deflated text in `UploadChunk` pieces; the server parses it off the game thread, builds it step by step on a scratch world through the normal rules (`ConstructBuild.BuildSteps`), and refuses a part it doesn't have by name.

**Wire**: `CActKind.BuildAid` with `BuildAidOp.Select`, then a `Game.SelOp` (`GameSandboxSelect.cs`). Server: `ConstructEditsSelect.cs`. Client: `SelectController.cs`, `Program.SandboxSelect.cs`, `ConstructRenderSelect.cs`, `UiSandboxBlueprints.cs`.

Choices made (the developer may change them):

- A selection is one construct. Aiming at another replaces it.
- Free landings freeze; welded ones don't.
- Nothing on the wheel: a tenth segment moves every tool on it.
- Physics-only parts are paid in loose bodies near the player.
- A stamped blueprint's fuel and charge are what fitting new tanks and batteries gives (full), as for any blueprint.

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
| `kick` | The recoil a shot, a barrel (N s): an impulse back along the barrel at its tip, through the joints under the gun and the body. |
| `fx`, `soundOf` | `bullet`, `cannon`, `rocket` or `flame` (how its tracer and flash look); the weapon file whose report it uses. |

An ammo box's block: `"ammoBox": { "rounds": 300, "cookOff": { "radius": 3.5, "damage": 260 } }`. A gun part's:
`"gunPart": { "part": "Long Barrel" }` (barrels, splitters, feeds, ammo, muzzle devices and optics only).

A bomb (any role): `"explosive": { "radius": 14, "damage": 260, "fuse": 1, "knockback": 18, "crater": 5 }`. When the part
breaks it is armed, and after `fuse` seconds it goes off where it broke with an explosive prop's blast (players, props,
buildings, constructs, fireballs; `crater`: the crater it digs, as a prop's "crater"), credited to the construct's owner,
so one bomb sets off the next. The Aerial Bomb and Small Nuke parts (Weapons > Bombs) use it.

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
  heavier than the Buggy without its tow hitch).
- **Recoil** (board item gr01; `ConstructWeapons.Recoil`, `ConstructPhysics.AddRecoil`): each shot pushes back along
  the barrel with the gun's `kick` (N s) at each barrel tip (a splitter's every muzzle; a merged receiver's kick is
  already scaled by the merge rules), as a real impulse on the gun's segment. On the body it pushes and twists the
  vehicle (an off-centre cannon yaws a buggy, a high one pitches it back). Up a joint chain each turntable, motor hinge
  or piston takes the part about (or along) its axis as a jolt, and its motor holds or gives by the strength model: a
  stock turntable kicks a few degrees under a cannon and comes back, an elevation hinge climbs and recovers, a weak or
  unpowered joint visibly gives, and an MG's sustained fire walks an unpowered turntable round. What a joint's motor
  and stops push back with passes into the body (an anchored body doesn't move; its joints still kick). The shots of a
  tick add up per segment (no allocation). Clients see it through the poses (no wire change); the gunner (in the gun's
  seat, or the driver of its body) gets a small camera shake to the pull's kick (`GunnerShake`: an MG a tremor, a
  Cannon about 0.6). Kicks: Mounted MG 12, Autocannon 120, Cannon 900, Rocket Pod 10 (rockets are nearly recoilless),
  Flamethrower 0; receivers 6 (Rifle), 8 (Shell), 25 (Heavy), generated receivers `receiverKick` (6).

### The yard

Construct Yard gains a **Gunner Turret** (a turntable, a gunner's seat on the arm, a Mounted MG on a motor hinge at the
front and an ammo box: sit in it, look, fire) facing a **Target Wall** (a steel panel on a post, a fuel tank and an ammo
box behind, a wood panel on the side); a **Hood Gun Buggy** (the buggy with a Mounted MG on its hood, fired by the driver's
fire key through an input reader); a **Sentry** (a Mounted MG on a turntable with a 6 m proximity sensor wired into it,
south-east, away from the spawns); a **Gun Bench** (a weapon builder gun from parts: a rifle receiver, barrel, muzzle
brake, box magazine fed by an ammo box, AP rounds and a scope, fired by a switch on its base) pointing at an **Ammo Dump**
(two crates: shoot one, and it sets off the other). Each is a blueprint in the parts menu too. The rack hands out every
weapon part.

### Decisions made for construct weapons (the developer may change them)

1. **Feed by touch, not wires**: ammo boxes feed a gun when bolted to it or its gun parts, or through feed chutes, like
   fuel through fuel lines (wires stay for signals).
2. **A receiver gun is its parts on the construct**: barrel, splitter, feed, ammo, muzzle device and optic are each a
   construct part naming a weapon builder part (13 hand-made; since wp01 every other builder part is generated, see
   "Weapon builder parts in Sandbox"). The action comes with the receiver part (its stock action, or one named in its
   file) unless an Action part is bolted on; grips, stocks, upgrades and utility parts take a free builder socket the
   builder's rules allow.
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

## Weapon builder parts in Sandbox

Board item dev-sandbox-weapon-parts-wp01 (decision 23, 2026-10-30). Code: `Source/Constructs/ConstructWeaponParts.cs`;
rules: `Constructs/WeaponParts.json`; self-test: "Sandbox weapon parts" (`Source/App/SelfTestSandboxWeaponParts.cs`).

- **Every builder part is a wrench item.** Every part in `Parts/<Category>` (guns, melee and grenade parts) has a Sandbox
  construct def, so it can be held, stowed in the backpack and storage slots, set down, and wrenched onto a construct.
  The defs are **generated at load** from the part library, never written by hand: each builder part becomes a part
  file's text that goes through the ordinary parser (so it is checked like any file): its model's boxes (tagged boxes
  left out, each as the box round it turned, the biggest 15 kept) scaled by the rules' `scale` (2: the hand-made
  construct gun parts are about twice the handheld size) and centred on their bounds, a hidden solid box round them, a
  socket on each face (right, left, top, bottom, back, front), the builder's weight times `massScale` (at least 0.5 kg),
  health `healthBase + healthPerKg x mass`. They are wrench-fitted and light (at most `ConstructParts.HandCarryLimit`, 40 kg), so
  they are always items, never physics-only bodies (decision 21; the self-test checks it). A receiver becomes a `gun` on that receiver (its stock action, else the
  first action its socket takes) with a **bottom** mount like the hand-made ones; every other part a `gunPart`.
- **Ids.** The generated defs come after the hand-made files, in the builder's own sorted order, so the hand-made parts
  (and saves and blueprints naming them) keep their ids, and every peer with the same files gets the same ids (peers
  check the build on join). A builder part a hand-made file already wraps (the 13 gun parts and the three receivers) is
  not made again. With today's files: 300 generated, ids 451 to 750 of the 4096 budget. A name a construct part already
  has gets " (W)".
- **The inventory.** They are in the Tab inventory's **Weapon parts** category with the hand-made receivers and gun
  parts, the builder's categories folded into eight sections (the search still finds "muzzle" or "splitter"), with their 3D tiles. They are not on the
  parts rack by default (a map's `rackParts` can still name them).
- **Creative-only.** The rules' `creativeOnly` categories (`upgrade` and `multiplier`, the Zombies-economy parts) are
  offered only with infinite parts: the parts menu refuses them on a map with `"infiniteParts": false` ("the X is
  creative-only (a Zombies-economy part)"). Question for the developer: should they ship in survival?
- **On construct guns.** Any builder part can be wrenched onto a construct receiver. Barrels, splitters, feeds, ammo,
  optics and muzzle devices go where they did; an **Action** part bolted on replaces the receiver's stock action; any
  other part (grip, stock, utility, upgrade, multiplier...) takes the receiver's first free builder socket that
  `BuildRules.SocketWhy` allows, or the gun's look line says the builder's reason. The numbers come from
  `BuildStats.ReceiverStats` as before, so a construct gun has the Zombies builder's numbers for the same parts.
- **Merging.** Every generated receiver is a receiver gun, so the merge rules (`Constructs/ReceiverMerge.json`) apply to
  all of them: a merged NxN receiver has exactly one front barrel socket, at its front centre.

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `scale`, `massScale` | Model size and mass against the handheld part. |
| `healthBase`, `healthPerKg`, `armour` | Health and armour. |
| `attachSeconds`, `receiverAttachSeconds`, `receiverKick` | Fitting time; a receiver's kick a shot. |
| `creativeOnly` | Builder categories offered only with infinite parts. |
| `skip` | Builder categories never generated. |

## The Gun Workbench (wp01, part 2)

Code: `Source/Game/GameSandboxBench.cs` (the server's rules and the sync), `Source/Constructs/ConstructGunBench.cs` (the
parts at a bench), `Source/UI/UiWorkbenchRun.cs` (the real workbench window in a Sandbox mode), `Source/UI/UiSandboxBuilds.cs`
(the Builds tab); part `Constructs/Parts/Storage/GunWorkbench.json` (a storage part with `"storage": {"gunBench": true}`:
six slots on its top, two long ones for barrels); self-test "Sandbox gun bench" (`Source/App/SelfTestSandboxGunBench.cs`).
Protocol 96: `CActKind.Bench` (kinds 6 open, 0 build, 3 take apart, 7 spawn a standard build); `Msg.ZBuild` and `Msg.ZRun` are
now also routed in Sandbox.

- **A handheld built weapon.** The Zombies path, reused: a gun goes in slot 1 (`Player.BuiltPrimary`, replacing the
  loadout primary), a melee weapon in `MeleeBuild`, a grenade in `GrenadeBuild`; one of each kind. Synced as the build's text
  (`Msg.ZBuild`), so every PC assembles the same numbers (`BuildStats`); a joiner is sent everyone's builds. Ammo is endless in
  Sandbox. It is used with bare hands (tool 1/2), like any gun. It survives death; a new match clears it.
- **At the bench**, F (not on a lit slot) opens the same `UiWorkbench` window with slots Gun / Melee / Grenade. The server
  needs the player within `ConstructEdits.BenchReach` of a bench; the window closes when the player walks away.
- **Creative** (`infiniteParts`): every part, endless; building or taking apart consumes and returns nothing.
- **Survival**: the stock is the part in the hands, the backpack and the bench's slots (`Msg.ZRun` tells the window). A build
  is checked whole (a missing part refuses it and nothing is taken), then takes what it adds and gives back what it gave up.
  Taking it apart returns every part through `BenchGive`: the bench's slots first, then `ConstructEdits.Give` (hands,
  backpack, the floor). Creative-only parts have no survival item, so they can't be built with there.
- **Standard builds**: the Tab inventory's **Builds** tab spawns any of the 141 complete into the hands (creative only).

Decisions the developer may change: the gun replaces the loadout primary (taking it apart leaves empty hands in slot 1, not
the old gun); creative disassembly returns nothing; consumption order is bench slots, backpack, hands; no auto-switch of
tool when a weapon is built.

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
the couplings, the wires between constructs and the power.

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
3; angle 4 kg and channel 6 kg, armour 6 and 7. Shapes are solid steel (armour 8). Armour class parts recycle at a higher rate than steel (the recycling table). Wood and glass are fitted with the wrench, everything else with the welder;
heavy armour takes 5 s to weld.

**Shapes**: a part box may take one of the weapon builder's contoured shapes (`"shape"`: wedge, arc, cylinder, bevel,
rounded...; `Source/Zombies/PartShapes.cs`, Parts/README.md "Shapes"), but only as a detail box (drawn only): collision
and hit areas stay boxes, so a shaped part stands on **hidden** solid boxes (`"hidden": true`: solid, never drawn) that
step along its shape (a wedge's slope in 4 to 12 steps). The shape turns with its part (`CPartBox.Model`); the tiles,
ghosts and the part in your hands draw it too.


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

## Size classes (the capital parts)

The developer's request (2026-11-03): "all the parts that are in the sandbox mode are what I would call small vehicle
parts. I'm planning on vehicles getting quite large. Compare the size difference between a plane atop an aircraft
carrier." The gameplay is a mothership launching small vehicles: a carrier (300 m, 100,000 t) or a Mortal Engines traction
city (a tracked land mothership under a multi-storey town). Smelters reach 10 m and Storage Volumes 40 m already.

| Class | Scale | Cell (lattice) | Plate | Frame members | What it builds |
| --- | --- | --- | --- | --- | --- |
| Small | x1 | 0.25 m | 5 cm, 40 kg/m2 | 0.25 to 2 m | a buggy, a plane (every part before this; they stay as they are, group by family) |
| Medium | x4 | 1 m | 10 cm, 80 kg/m2 | 1 to 8 m | a truck, a tank, a bomber; balconies and rooms |
| Large | x16 | 4 m | 20 cm, 200 kg/m2 | 4 to 32 m | a hangar, a storey of a town, a big walker, a frigate |
| Capital | x64 | 16 m | 50 cm, 500 kg/m2 | 16 to 128 m | a carrier's hull, a traction city's chassis |

**One helper**: `tools/constructs/scale.py` holds the classes (`SMALL`, `MEDIUM`, `LARGE`, `CAPITAL`) and what every big part's
generator shares: `Part`, `box`, `block_sockets`, `write_parts`. `tools/constructs/capital_parts.py` writes the structure
(`Constructs/Parts/Capital`, 441 files; `--check` compares). Part files say `"sizeClass": "capital"`; a socket takes its part's
class unless it says `"class"`.

**Sockets.** A larger class's part is made of its own cells and its sockets step on its own coarser lattice (a capital plate
has a socket every 16 m, so 64 sockets a part are plenty). A socket snaps, autolinks, joins and socket-welds only to a
socket of its own class, and a mismatch says "use an adapter". The freeform and point welds (one part anywhere on another)
ignore classes.

**Adapters** (section Adapters, brass pads) join two classes: one socket of the big class on the back, a 5 x 5 grid of the
smaller class on the front (`Adapter Capital to Small` and `...Wide`, `Adapter Capital to Large`, `to Medium`, `Adapter Large to
Medium`, `to Small`, `Adapter Medium to Small`). A small part's whole-centimetre origin is kept: the pad's back is on a
whole centimetre and a small-class front half a one off it.

**The kit** (Medium, Large and Capital unless noted; the inventory has a group per class, a section per family; search finds
any part, and the tree stays usable at 441 more tiles). Traction-city pieces first, because a land mothership cares about
ground bearing and height:

| Section | Parts |
| --- | --- |
| Frames | frame node; girders 1 to 8 cells; hollow box frames; open trusses (chords collide, lacing is drawn); round columns; 45 degree struts; stepped buttresses; stepped corbels that let a storey overhang |
| Chassis (Large, Capital) | Chassis Deck grillages up to 128 x 64 m: deep beams between two plates, sockets underneath for track units and on top for the town; tapered Load Spreaders |
| Plates, Armour | hull plate, rough riveted plate; armour plate and armour belt, rivets drawn; 1 x 1 to 4 x 4 cells |
| Walls | brick and plaster walls; window walls with real openings; arcades of 1 to 3 arches, stepped spandrels with a drawn arch ring |
| Decks | steel, timber and grate decks; railed balconies; terraces with a parapet on three sides |
| Roofs | domes and onion domes (stacked frustums), spires, hip, gable and barrel roofs |
| Stacks | banded riveted smokestacks and brick chimneys, up to 32 x 128 m; collision is a square of walls |
| Shapes | curved plates, wedges |
| Stairs | a flight (every step within the 0.55 m a player walks up) and a steep ship ladder for one cell, at every class |
| Hull (Large, Capital) | hollow hull sections (a floor, a ceiling and two sides, open ends), bows, keels |

A hollow part spends boxes where it shows: state of it, about 5.5 boxes a part in the test hull. A part has at most 32
boxes and 64 sockets and reaches at most 200 m from its origin.

**Limits**, and what set each:

| What | Before | Now | What stopped it there |
| --- | --- | --- | --- |
| Parts a construct | 96 | 8,000 | stack spans sized to the part count, on a 1 MB main thread on Windows; indices are u16, so up to 65,534 with those moved to scratch arrays |
| Links | 288 | 24,000 | three a part, indexed by part (`Construct.FirstLink`) |
| Joints + freeform welds | 16 (8 welds) | 255 (64) | a segment is a byte everywhere |
| Wheels and track rays, spring feet | 12 | 256 | a raycast each a tick; the wire doesn't care |
| Guns | 8 | 64 | a turn through the gun step |
| Routed links, nodes | 64, 768 | 254, 3,000 | a route's parent is a byte with 255 meaning none (a hundred sites and the tests); widening it is mechanical if a ship needs more |
| Wires | 64 | 1,024 | |
| Part mass, health | 50 t, 100,000 | 50,000 t, 16,000,000 | health is u24 on the wire |
| Part reach, sockets | 5 m, 40 | 200 m, 64 | |
| Piston travel, hitch reach | 4 m, 1.5 m | 64 m, 64 m | |

Every per-construct array is sized to the construct now and grows by doubling on an edit (`ReserveParts`, `ReserveLinks`,
`ReserveJoints`, `ReserveGuns`, `ReserveRoutes`): a loose block costs less than it did and a world of thousands stays small.
Nothing grows in a tick.

**Keeping it fast.** Nothing that runs per tick or per frame walks a big body's parts any more:

- `Construct.FirstLink` / `NextLink`: a threaded list through the links, so `SocketUsed`, the split flood, the circuit and the
  drive line's alternators cost a part's own links. `AddLink` keeps it up; a compaction calls `LinksChanged`.
- `ConstructPartTree.cs`: a tree over the rigid parts (rest frame, built on first use after a rebuild; parts on moving
  joints and parts added since are always handed back). Rays, a player's blocking, grab-ons, placement and ghost checks ask
  it (`PartsInBox`, `PartsOnRay`). Under `TreeMinParts` (32) they walk the list as they always did. `ConstructCollider`
  (feat/collision) has its own tree over boxes for contacts; the two can merge later.
- Physics: joints are laid out end to end for the bodies simulated (`LayOutJoints`), not a fixed 255 slots a body. The inertia
  inverts in doubles (a float determinant of a 100,000 t hull's terms is close to overflowing). Spin counts as still by what
  it does at the body's far end, so a slow 300 m hull doesn't fall asleep rolling.
- Drawing (`Source/Render/ConstructRenderBatch.cs`): a body of 64 parts or more keeps its plain structure as boxes in the
  body's frame in a GPU buffer, one instanced draw at its pose (`Renderer.DrawBody`, the same box shader with a `uBody`
  matrix). Only what changes frame to frame or has its own mesh is walked: joints, wheels, logic, glass, burning parts, and
  plain parts' contoured boxes (one shared mesh draw each). The boxes are remade when the body rebuilds, or a look each half
  second finds health, paint or fire changed. A big body is never drawn as one far box, and is drawn to 3,000 m.
- Network ([below](#network-protocol-82-84-for-free-placement-86-for-the-drivetrains-rpm-87-for-glass-shattering-88-for-the-physics-gun)): u16 indices, positions in 3 bytes a coordinate
  (+-83 km on the 1 cm grid), and patches.

**Numbers** ("Capital parts", "Capital network"; a generated body of 4,050 parts, 100,000 t, 300 x 108 x 4 m, through
`ConstructBuild.Place` and `Attach`; the developer's PC VM, or the loaded cloud box where it says so, wall clock). A body of this
size could not exist before, so "before" is the old method measured or counted on it.

| What | Before (the old method at 4,050 parts) | After |
| --- | --- | --- |
| Rebuild | not measured; the circuit, the split and `SocketUsed` were each parts x links (about 40 million steps) | 2.4 ms (9 ms loaded) |
| Tick, dragged, awake, on flat ground | not measured | 0.01 ms median, 0.01 ms worst (4 ms worst loaded), 0 bytes allocated |
| A ray | 1,094 us (every solid box of the 4,050 parts, measured) | 2 to 10 us |
| A player's blocking, a tick | 22,000 boxes, about the ray's cost | 4 us |
| A part off the middle | not measured | 2.6 ms (21 ms loaded) |
| Draw calls | 22,275, one a box | 1 instanced draw; 0 parts walked for this plain hull |
| Whole body on the wire | 133 KB per edit | 133 KB once; an added part 125 to 144 B |
| Client receives it, loopback | | 0.10 s |
| Late joiner, loopback | | 0.10 to 0.15 s |

**Not in budget yet:** a small vehicle on the deck. A buggy resting on the hull worst-cases 23 to 39 ms a tick, because the
body-on-body contacts still walk the big body's parts (`ConstructPhysics.BodyContacts`, 40 hull points x every part). That is the
compound collider's work (feat/collision), and "Capital parts" prints the number without failing on it.

**Choices made (the developer may change them)**: the physics gun lifts 20,000 t in creative with at least 8 m/s2 (it was 15 t), and survival stays at 500 kg, so a big part there goes on with the socket weld or a crane; four classes at x4 steps; strict socket classes with adapters, and the
freeform welds as the escape; a stair is one cell at every class; plate thickness and kg/m2 by class; the game's own soft
density (a 500 kg/m2 capital plate is a fifth of real steel at 50 cm) so that 4,000 parts come to about 100,000 t; hit points
by area, capped at 16 million.

## Building pieces

Board item dev-sandbox-building-pieces-bp01 (the developer's note, 2026-10-30): large prefabricated parts for anchored
builds (bases, bunkers, towers) in the inventory's **Buildings** category. They are written by
`tools/constructs/building_parts.py` into `Constructs/Parts/Building` (178 files; run it again after changing a family,
`--check` compares), and the same script writes the four example blueprints (it mirrors the game's Rot24 turns and fitting
order to work out each step's sockets from where the part should go).

**The module.** The pieces keep the structural parts' 0.25 m cell rules (origin on a cell centre, sockets on cell faces at
cell centres), so they line up with the beams and panels. Walls are 0.25 m thick and 2.75 m tall; a floor slab is 0.25 m
thick, so a storey is **3 m floor to floor**. Lengths are **2 m and 4 m**. A room's corners are **L-shaped corner pieces
with 2 m legs** (8 cells each, sharing the corner cell): four close a 4 by 4 m room, and straight 2 m and 4 m walls fill the
module between corners on bigger rooms (4 by 6, 4 by 8...). An inside wall meets an outside one at a **Wall T** (a 2 m
crossbar, a 1.75 m stem), a free wall ends in a **Wall End**. Floors, foundations and bunker roofs have sockets at the
module points (cells 0, 7, 8 and 15 of each side) where walls stand; a slab on the walls' tops welds to every one it
meets, and the next storey's corners stand on the slab.

| Section | Parts |
| --- | --- |
| Walls | per material: wall 2 and 4 m, half wall 2 and 4 m (1.25 m), parapets 2 and 4 m (0.75 m; wood and concrete) |
| Wall openings | window walls 2 m (one 1 m window) and 4 m (two), sills 1 m up; door wall 2 m (1.5 by 2.25 m doorway, a hinge socket on each jamb); garage wall 4 m (3 by 2.5 m); slit wall 2 m (two 0.5 m slits at 1.3-1.65 m, eye height); Window Glass 1 m (fits a sill) |
| Corners and ends | per material: wall corner, door corner (a doorway in one leg), window corner, wall T, wall end; parapet corner |
| Floors | wood, steel, concrete, reinforced: 2x2, 2x4, 4x4 m; hatch floor (1 by 2 m hatch for a ship ladder), stair floor (1 by 3 m opening a cell in from one side), stairwell floor (2 by 2.5 m) |
| Foundations | concrete 2x2 and 4x4 m pads (0.5 m), stepped (its back half 0.5 m deeper, for slopes), deep (four piers 1 m down), reinforced 4x4 m |
| Roofs | roof slopes 4x4 and 2x4 m (wood, corrugated; 1 m rise), corrugated lean-tos, ridge roofs 4x4 m (one piece, closed ends), gable walls (close a ridge made of two slopes) |
| Stairs and access | straight stairs 3 m (wood, steel, concrete: eight 0.375 m steps over 3 m), stair flights 1.5 m, half-turn stairs (2 by 2.5 m, a storey), Ship Ladder 3 m (steep: 0.375 m steps over 0.25 m), landings 2x2 m at 1.5 m, ramps (concrete 2x4 and 4x4, steel 2x4, wood 1x3 m), railings 2 and 4 m (steel, wood) |
| Doors and gates | Wood and Steel Door 1.25 m, Garage Door Leaf, Fence Gate: door-role leaves hung on the existing **Hinge** (so Interact and logic open them; a hinge's base goes on a jamb's socket) |
| Defences | sandbag walls 2 m, 1 m, low 2 m, corner; Concrete Barrier 2 m; Blast Wall 2 m (3 m T-wall); wire-topped Wire Fences 2 and 4 m (seen and shot through, not walked through); Bunker Module (4 by 4 by 3 m, slits, a back doorway) and Pillbox (3 by 3 m), both foundations that anchor themselves |
| Watchtower | Tower Platform 2x2 m (parapet on three sides), Tower Brace, Tower Post 1.25 m, Tower Roof; the legs are Wood Posts |
| Columns and beams | concrete, steel and wood columns 2.75 m; beams 2 and 4 m |
| Damaged | the walls' damage stages (not offered) |

**Materials** (per square metre of wall; walls burn by their material, as before):

| Material | kg | Health | Armour | Tool |
| --- | --- | --- | --- | --- |
| Wood | 30 | 45 | 2 (burns) | wrench |
| Corrugated metal | 18 | 55 | 4 | wrench |
| Concrete | 300 | 260 | 25 | welder |
| Steel plate | 160 | 320 | 35 | welder |
| Reinforced concrete | 360 | 420 | 45 | welder |

A part's mass may now be up to 50 t (a 4 m pad is about 14 t; was 5 t).

**Walls lose chunks.** The straight walls (2 and 4 m, every material) have two damage stages (`"damaged"` in the file):
below 60% of its health a wall turns into its **Damaged** stage (a 1 m chunk gone from a top corner), below 30% into
**Breached** (also a hole at the bottom, 1 m wide and 1.5 m tall: crouch through it), and at zero it breaks off as before
(`ConstructWorld.LoseChunk`). A stage has exactly the wall's sockets, so every weld stays; its health carries on from the
wall's. The structure goes out again (the part's id is in it): **no wire change**. Window, door and other walls break
whole.

**Walking.** `ConstructWorld.BlockPlayer` now steps a player (and a bot: the same code runs for every player) up onto the
boxes of anchored constructs and of parts marked `"walk"` once the player's disc reaches them, up to `World.StepHeight`
(0.55 m) above where the feet were that tick, as the map's own steps are: stairs, ramps, the ship ladder, onto a pad or a
low sandbag wall. A box whose top is below the feet no longer pushes (so walking from one floor slab or step onto the
next never snags). Loose and free constructs are unchanged (a parked car still blocks you).

**What bots still need** (the sandbox-spawns work owns bot navigation over constructs): today bots walk up stairs and
through doorways only when they already walk that way (their nav graph is the map's). For paths into and through
buildings they need the constructs' walkable tops as nav nodes (every `"walk"` part's top boxes; `Floor`, `Stairs`,
`Ramp`, `Ladder`), links through door openings (a door leaf is a passive joint: open it with Interact first, as players
do) and a rebuild when a construct is built, changed or breaks.

**Blueprints** (`Constructs/Blueprints`): **Two-Storey House** (21 parts: two pads, a 4 by 8 m concrete house with a
front door, window walls on both floors, a straight stair under a stair floor, two wooden ridge roofs), **Bunker** (11:
a Bunker Module on two pads with a steel door onto a sandbagged yard), **Watchtower** (15: a platform 3 m up on four
posts with three braces, a ship ladder and a corrugated roof) and **Small Outpost** (15: a corrugated hut with a door, a
fenced yard with a gate, sandbags at the front).

**Choices made (the developer may change them)**: corners are L pieces with 2 m legs, so a room's sides are at least
4 m (2 m walls go between corners); walls are 2.75 m under a 0.25 m slab; stairs rise 0.375 m a step (45 degrees on the
straight stair, a storey in 3 m); the ship ladder is a steep stair rather than a climbing mechanic; stair floors' openings
sit a cell in from the edge so the stair runs along a wall; doors are new 1.25 m leaves on the existing Hinge (the
milestone 1 Door is 1 m); only straight walls lose chunks, in two fixed stages, and the lost chunk leaves no rubble piece;
windows are open (a Window Glass fits each); the bunker and pillbox are foundations; masses are realistic-ish (a concrete
4 m wall is 3.3 t), which needs the 50 t cap.

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
  while anyone sits in either, or past the caps ([Performance and caps](#performance-and-caps)).
- **Ownership and limits**: what a player sets down (or claims, or joins) is theirs (`Construct.Owner`; factions later).
  A player may own **12** constructs (wrecks and loose parts don't count); past that a new one is refused ("take one apart
  or join two"; the host may change the number). The world has no fixed caps since board item cl01, only the host's
  safety cap ([Scale](#scale-thousands-of-constructs)).
- **Network** (protocol 84): `CAct.PlaceNew` carries the surface point, its normal, the yaw, the turn and two flags (grid,
  anchor); the server finds the surface again under the point itself. `CAct.Join` names the part. CBody's flags carry
  "pinned". Everything else (the new construct, the merged one, the one that went) travels as before.
- **Cost**: the client runs the pose, the fit check and the join search every frame while a part is held: fixed loops over
  the bodies near it, no allocation (the self-test measures it).


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
- **Drills**: a shaft into a Drill Module's Drive in turns it, with or without a transmission, and it takes its torque back
  from the engine: [The drill's buffer and shaft](#the-drills-buffer-and-shaft).
- **PTO**: a turntable, motor hinge or piston bolted to a power take-off on the drive line runs from the engine, only while
  it runs, with no battery. An unwired turntable on a take-off turns with the engine (its rpm's share of the limiter;
  backwards on a reverse throttle); a wire into it still sets its speed.
- **Standalone parts** (board item cs03, decision 32): nothing waits for the construct to be a "vehicle". The drivetrain
  builds with or without a seat (`Construct.HasPowertrain`: an engine with what it runs on and a transmission on it;
  `CanDrive`: that and driven wheels). The engine runs for a driver or a signal: an engine has two data ports, **run**
  and **throttle**. Wired on run alone it runs at that signal (the old single input: a lever is the throttle); with the
  throttle port wired too, run (0.5 or more, or unwired) starts and stops it and throttle sets the throttle (a lever, a
  number from the logic). On an **anchored** construct (a foundation, pinned, frozen) the engine still runs and the
  wheels don't roll it: each driven wheel turns at the engine's speed through the gear (`ConstructDrive.AirOmega`), and a
  turning wheel's tread drives what sits on or leans on it through the contact friction (`ConstructPhysics.TreadVel`):
  a crate on it, a vehicle against it, a row of them is a conveyor. A **suspension with nothing on its axle, pointing
  down**, is a spring foot (`CSpring`): a raycast spring like a wheel's with a gripping foot, so a platform on
  suspensions stands on its springs, and players standing on it weigh it down (a landing knocks it). Sleep: an anchored
  construct whose engine runs, runs down or still turns a wheel is stepped every tick (`ConstructPhysics.EngineLive`,
  and what sits on its turning wheels is woken); once all has stopped it costs nothing again. Not yet: a spring foot on
  an anchored construct can't give (the body can't move; that needs a sprung joint), and a turning wheel only drives
  bodies whose pieces touch it, not a body it is itself pressed into.
- The configure tool is still to come: the **HUD** shows the speed, rpm, torque, gear and fuel while driving (and "R gear
  up, Shift gear down" for a manual), and the **look line** an engine's rpm, torque and curve, a gearbox's ratios and kind,
  a differential's kind and ratio, a transfer part's ratio or state (2WD/4WD, engaged), a wheel's size and grip (FLAT), a
  suspension's travel and preset or height.

### The families

| Family | Parts (no single best: mass, grip, power, fuel and toughness trade off) |
| --- | --- |
| Wheels | Wheel (0.8 m road), Small (0.6 m), Large (1.1 m), Off-Road (grips dirt, worse on roads), Monster (2.4 m, 220 kg), Solid (no puncture, armour 25, less grip), Slick (most grip on hard ground, slides on dirt), Spiked (bites dirt, skates on roads), Caster (0.4 m, swivels, can't be driven), Omni (slides sideways), Tank Road Wheel (steel), Track (5 m) and Short Track (3 m), sprockets at the back |
| Suspensions | Suspension (0.3 m, 600 kg), Stiff (0.12 m), Long-Travel (0.6 m, soft, light loads), Leaf Spring (3 t a wheel, tough), Adjustable (soft, medium, stiff, high, low), Rigid Mount, Hydraulic (active); Pogo Struts, Swing Arms, Double Wishbones and Trailing Arms in eight sizes: [Suspension types](#suspension-types) |
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
note; eight vehicles driving at once allocate nothing (about 0.2 ms a tick in the container).

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

## Suspension types

Data: `Constructs/Parts/Suspensions` (36 parts) and the demo buggies, written by `tools/constructs/suspension_parts.py` (its
tables at the top are the balance; `--check` compares; `MENU` there is the one place that names their menu group). Code:
`Source/Constructs/ConstructSuspension.cs`, the rigged boxes in `ConstructRender.RigOf`.

**Sizes go by wheel.** Every kind comes in eight sizes, and the description says which wheels a size takes and what it carries:

| Size | Wheels | Carries a wheel | Travel | Spring |
| --- | --- | --- | --- | --- |
| Kart | 0.2-0.3 m | 150 kg | 0.12 m | 3.2 Hz |
| Light | 0.3-0.45 m | 400 kg | 0.2 m | 2.6 Hz |
| (no word) | 0.4-0.55 m | 1 t | 0.3 m | 2.2 Hz |
| Heavy | 0.55-0.8 m | 2.5 t | 0.45 m | 1.8 Hz |
| Truck | 1.2 m (Monster) | 8 t | 0.8 m | 1.4 Hz |
| Medium | 1.5 m (Medium Tyre) | 60 t | 1 m | 1.4 Hz |
| Large | 6 m | 3,000 t | 3 m | 1.1 Hz |
| Capital | 24 m | 60,000 t | 8 m | 0.9 Hz |

Nothing refuses a wheel for its size: the sockets fit, and the rating is what bites. The new kinds are gas struts (`ride`), so
each rides half way down its travel whatever it carries up to its rating, its arm level; past it, it sags and bottoms, as any
suspension does. The spring's stiffness comes from the load (`ConstructPhysics.Wheels`), so a size bounces at its frequency
from a light body to its rating. Stiffness and damping are configure tool settings on all of them.

**The kinds**, and how each mounts:

| Kind | Mounts by | Moves |
| --- | --- | --- |
| Suspension (plain; Kart, Light, Heavy and Truck added to the older parts) | `mount` (side), `top` | Wheel straight up and down beside the bracket. Nothing else moves. |
| Pogo Strut | `mount` (its top end, under something), `side` (its cap), `inner` (beside the hub, for a differential's end or a steering pivot's leaf) | Wheel straight along the strut: the rod slides into the tube, the coil closes up. |
| Swing Arm | `mount`, `mount-high` (chassis side), `top` | Arm hinged on the chassis side, wheel fixed to its end: the wheel swings on an arc, leans with the arm, and the track changes. A damper stands on the arm. |
| Double Wishbone | `mount`, `mount-mid`, `mount-high`, `top` | Two arms, an upright between them: the wheel moves on the arc but stays upright. |
| Trailing Arm | `mount`, `top`, `bottom` (the hinge's end) | Arm along the car, hinged ahead of the wheel: the wheel rises on an arc, moving fore and aft. Mirrored top to bottom, so rolled 180 degrees on its socket it trails on the other side of the car too (or leads); the Mirror Table does this itself. |

There is no Leaf Spring on a solid axle: the game has no beam axle (a Live Axle is a locked differential bolted to the body, and
each of its wheels springs on its own), so nothing would tie two wheels together. The shipped Leaf Spring stays as it is.

**The wheel's path** (`"path"` in the `suspension` block): `{"kind": "line"}`, the default, or `{"kind": "arc", "pivot": [x, y, z],
"axis": "+z", "length": m, "tilt": 0 to 1}`. `pivot` and `axis` are the arm's hinge in the part's frame; the hub swings in the
plane square to the axis. `length` is the arc's radius, when it isn't the hinge's distance from the wheel (a parallel-arm
upright's). `tilt` is how much of the arm's angle the wheel leans by. An arm too short for its travel (under 0.6 of it) is
refused. The raycast wheel keeps its role, and stays one ray and one spring: the ray still runs straight down the body and
`Compression` is still how far the ground pushed the wheel up. The arc only moves where that ray starts (`Construct.WheelFrame`,
along the arm, from last tick's compression), and so where the contact is, and where the wheel is drawn. `CWheel` keeps the arm
in the body's frame (`BuildArm`), so an arm works in any of the part's level turns and on a steering pivot's segment. A hinge
turned to run up leaves the wheel on a straight line; the look line says "stood on end: moves straight". Camber is drawn; it
doesn't change the tyre's grip.

**Boxes that move** (a detail box's `"rig"`): `"hub"` goes with the wheel; `"arm"`, with its `"pivot"`, turns by the arm's angle;
`"leg"`, with `"from"` (fixed) and `"to"` (moving, at rest), is drawn stretched between the two: `"pivot"` turns its moving end,
without one it moves with the hub, and `"span": [a, b]` draws that share of the way (a sleeve, a coil's ring). A leg's file box
gives its width, and stands straight at rest so a ghost and an icon look right. The solid boxes don't move: each part is a
hidden solid at rest, what collides and what the sockets sit on.

**Driving.** Nothing changes for the drive line: a suspension is driven by its link to a differential, a hub or a fed steering
pivot's leaf, whatever its kind, and refusals read as before ("a steered wheel needs a U joint or CV joint to be driven"). A
Pogo Strut goes on a differential's end or a pivot's leaf by `inner`. On a steering pivot the whole arm turns with the steering,
so a swing arm's wheel swings round the pivot on the arm's length: keep steered arms short. A Trailing Arm moves its wheel a cell or
more back along the car, which shifts the load towards the other axle: the Trailing Arm Buggy pulls away a little slower.

**Blueprints**: Pogo Buggy, Swing Arm Buggy, Wishbone Buggy and Trailing Arm Buggy are the Buggy on each kind, generated from it.

## Modular engines

Build your own engine from parts that snap together; the game recognises it as one engine, with no wiring puzzle. Code:
`Source/Constructs/ConstructEngine.cs` (recognising it, the sums, the readout), `Source/Render/EngineFx.cs` (pistons, shake,
smoke), `Source/Audio/SynthEngine.cs`, `EngineSounds.cs`, `Audio.Engine.cs` (the sound). Data: `Constructs/Parts/ModularEngine`
(40 parts) and `Constructs/ModularEngine.json` (the sums' numbers), both written by `tools/constructs/engine_parts.py`:
change its `SIZES` table and run it, never the output. Self-test: "Modular engine", "Modular engine network".

### The pieces

Ten kinds in four sizes (Small, Medium x4, Large x16, Capital x64 by linear size): the look is exposed and industrial, with
open frames, columns round a visible pumping piston, a spoked flywheel and tall stacks.

| Piece | Where it goes | What it does |
| --- | --- | --- |
| Crank Section | end to end; bolts to a frame by a side | the crank: its length is the engine's |
| Cylinder | on a crank section's side | adds power; which sides make the layout |
| Carburettor | on a cylinder's head | fuel system: feeds its whole bank, x1 power, x1 fuel |
| Injector Rail | on a head | fuel system: x1.2 power, x0.8 fuel a kW |
| Turbo | on a head or a fuel system | x1.4 on its bank, 1.5 s of lag, x1.15 fuel |
| Supercharger | on a head or a fuel system | x1.25 at once, x1.25 fuel |
| Flywheel | on the crank's end | the output: the drivetrain's engine: gearbox or shaft on it, run and throttle ports |
| Starter Motor | on a crank's side or the flywheel | Large and Capital need one, and a battery to turn it |
| Exhaust Outlet | on a head or a fuel system | a smokestack stands on it or takes a pipe from it |
| Smokestack | on an outlet or another stack; bolts to a frame | the smoke leaves the top one, thicker and blacker with load |

A working engine is four parts: a crank, a cylinder, a fuel piece and a flywheel, plus a fuel tank touching any piece of it or
piped into its fuel system or flywheel ("Fuel in" ports).

- **Layout**: cylinders are grouped into **banks** by which way they point from the crank: one bank is inline (I4), two at right
  angles a V (V8), two opposite a boxer (B4), three a W (W12), four a radial (R16). Sockets are square to the part, so a V is
  90 degrees; a freeform weld can set any angle.
- **Power** is the sum over the cylinders of the size's kW (40, 800, 16,000, 320,000) times the bank's best fuel system times
  its best booster. Torque is that power at the peak rpm (5000, 2800, 1400, 700; limiter 7000, 4000, 2000, 1000): big
  engines turn slowly. The curve is the petrol curve and the limiter is the soft limit. Fuel at full throttle is 0.00044 l/s
  a kW times the thirsts.
- **One fuel piece a bank** is enough, on any cylinder of the bank. A bank with none gives nothing and the readout says which.

### The readout

The look line on any piece, with any tool in hand, says what the pieces add up to and what is missing: "V8 320 kW, thirsty",
"V8 160 kW - no fuel system on bank 2", "no output: fit a flywheel on the end of the crank", "no starter: an engine this big
needs a starter motor", "no fuel: link a fuel tank", "mixed sizes: every piece of an engine is one size". Driving, the HUD's
red line gives the same reason. "thirsty" is 1.15 times the plain burn a kW or more, "frugal" 0.85 or less.

### How it is recognised

`ConstructEngine.Build` runs with the caches. Working pieces that touch (socket links, a seam, a freeform weld's bead) with the
same `MoveSeg` are one engine: a turntable between them breaks it, a freeform weld doesn't. Bank directions are compared in
the rest frame (`ToRest`, `DirToRest`). Everything is derived from the parts and links, so a client works out the same engine
and no save or message carries it. Building allocates nothing past a construct's first piece. `Apply` puts the numbers in one
`ConstructPartDef` (`Construct.EngineDef`) and `ConstructDrive.Tick` runs that: no tick work per cylinder. The flywheel is the
construct's `Engine`, so wires, gearboxes, shafts, PTOs, anchored bases and seatless carts work as for a fixed engine. An
alternator touching any piece counts. The first engine that can run is the construct's, as with fixed engines.

### Damage

A cylinder under half its health **misfires** (half its power); wrecked it gives nothing and its piston stops. The engine runs
on what is left, rougher, smokier, with a misfire in the sound, until every cylinder is gone. A wrecked crank section cuts off
what is past it from the flywheel. A health change only reworks the sums when it crosses the misfire line
(`ConstructEngine.HealthChanging`, on the server and on a client's health update).

### The show

All on the rpm and load every peer has (the load is the top three bits of the power flags, 0 to 7, protocol 122).

- Boxes tagged `"pump"` (metres) slide, `"spin": true` turn about the part's z; the drawn rate is watchable, not the true rpm.
- Big engines rock at idle and smooth out as they rev; the pieces are drawn shaken, the body isn't moved.
- **Smoke**: grey puffs at idle, a black column under load, from the tops of the stacks the engine feeds, else its outlets,
  else one head a bank. A misfiring engine smokes more. Flames lick from the stack at full load. A fixed pool of 256 puffs,
  drawn in the blended pass.
- **Sound**: a loop of the firing pulses per cylinder count (1 to 16, with and without a dead cylinder), pitched by the rpm;
  the four nearest running engines are heard, louder with load.

### Decisions made (the developer may change them)

1. A V is 90 degrees: part rotations are square. A V at a true angle would need angled sockets.
2. Any one fuel piece feeds its whole bank, with no limit on how many cylinders.
3. Starters are needed from Large up, and need a battery. Small and Medium start themselves.
4. The stacks and outlets have no effect on the numbers: they are looks.
5. No overheating: the rev limiter is the only soft limit.
6. Only modular engines make the new sound; fixed engines stay silent as they were. `EngineSounds.Pick` is where to change it.
7. Capital pieces needed `MaxPartReach` 50 m and a 500 t mass limit. Pieces from Medium up have the size class's sockets (`sizeClass`):
   they bolt to that class's frames, gearboxes and bunkers, or to smaller ones through a size adapter.
8. Pieces of different sizes never mate, and forced on they say so.

## Big drive

What moves and powers big constructs, in the Medium, Large and Capital size classes (the small class has a Hub Motor, a
Genset and two thruster sizes). The target is a traction city: a tall town on giant tracks, about 100,000 t and 300 m,
with a mothership route in the air second. Data: `Constructs/Parts/BigDrive`, written by
`tools/constructs/big_drive_parts.py` (its tables at the top are the balance: run it from the repo root; `--check`
compares). Code: `Source/Constructs/ConstructPlant.cs` (plants, stacks, the pools), `ConstructThrust.cs` (hub motors,
thrusters, the hover hold), the wheels in `ConstructPhysics.cs`. Self-test: "Large drive".

**No water, no lift.** The game has no water surface and no aerodynamics: no ship screws, no wings. The only ways a big
construct moves are the ground and a thruster.

The **Traction City** blueprint is the worked example: two 128 by 64 m Chassis Decks, four Capital Bogies on Hub Motors (the
front pair on Steering Pivots), two Boilers with Chimneys and Bunkers, a Battery Bank, and a bridge seat on an adapter. It
weighs 84,000 t and is a base to build a town on.

### On the ground (the main route)

- **Hub Motor** (`"suspension": {"motor": {"torque", "power", "top"}}`): a suspension with a traction motor in it, driving
  its wheel or track from the circuit, with no shaft. So several bogies under one body are each powered, and a bogie on a
  **Steering Pivot** is simply powered too (its base bolts to a body's side or underside). The driver's W and S work it;
  a throttle wire (its data port `throttle`, -1 to 1) works it alone, with no seat. With no steered wheel, A and D drive the
  sides against each other (skid steering), and a track keeps 0.4% of its sideways grip while it does
  (`ConstructPhysics.SkidSlack`): no hub motor beats the whole weight's grip. It draws from the circuit: no power, no
  push. A track's rays share its motor.
- **Tracks and Bogies**: wheels with a `track` block (long Tracks 4 to 6 contact rays, short Bogies 2 or 3). Their boxes
  with `"roll": true` are the exposed drive wheels and rollers, which turn on the spot as the construct runs.
- **Gas struts** (`"suspension": {"ride": 0.33}`): every big suspension holds its height whatever it carries (up to
  `maxLoad`), half way down its travel; a plain spring on a 100,000 t body would ride at the top of it and lift rays off
  the ground. **`slew`** (m/s): how fast the contact may follow a step in the ground (what the body does itself isn't
  held back: that lag fed a bounce), so a big track rides up a kerb instead of taking it in one tick.
- **Shafts** still work at scale: Diesel, Gas Turbine, Steam Engine and Electric Motor in the three classes, a Gearbox,
  Reduction, Right-Angle and T Gearboxes, a Clutch, CV Joint, Wheel Hub, two Drive Shafts, and an open and a locked Axle.
  A capital diesel is 8 MN m at 102 rpm; through its gearbox (12:1) and a capital axle (8:1) that is 27 MN at a 24 m tyre.
  `maxTorque` now rates a gearbox and an axle as well as a transfer part: past it, the drive line slips (the note says so).
- **Rolling resistance** is a share of the load at g 18 m/s2, so a big part's `roll` is 0.004 to 0.008; a small tyre's 0.012
  would eat a whole capital engine. **Air drag** thins above 20 t (it goes with area, the mass with volume).
- **Legs**: Leg Pistons, Jacks and Knee Hinges work by wire like other joints; Landing Struts are spring feet. A solve can
  load one leg first, so a leg is rated for nearly the whole weight (see the limits below).

### Power plants and smoke

- **Plants** (role `generator`: `{"rate": kW, "fuel": "fuel" or "none", "burnRate", "spool", "buffer"}`): a Boiler, a
  Furnace, a Genset and a Reactor. A plant feeds the circuit it is on with no engine and no seat, follows what the circuit
  asks (nothing asked: no fuel, no smoke), and comes up over its `spool`; its `run` wire holds it off, or keeps its fire
  banked at 10%. A plant makes a construct electrical like a battery, and holds its `buffer` (kJ). Fuel reaches it as it
  reaches an engine; a Reactor needs none.
- **Smoke** (`"smoke": {"at", "size"}`, any part): a stack puts out what a burning plant makes while one reaches it: the plant's
  own stack, a Chimney, Stack Section or Wide Funnel touching it (or touching a stack that does), or a pipe from its
  `Exhaust out` port to the stack's `Exhaust in`. An engine on fuel smokes as hard as it pulls. `Construct.SmokeLevel` goes to
  clients in the pose; the puffs are worked out from the clock (`ConstructRender.DrawSmoke`: no state, nothing allocated).
- **Steam engines** (`"curve": "steam"`): full torque from a standstill, falling away towards the limiter; their `spool` is
  raising steam.
- **Pools at scale.** A capital bunker holds 4 Ml and a bank 10 TJ, where a float can't take a tick's sip off: everything
  that burns or draws goes through `Construct.UseFuel` and `UseCharge`, which keep what doesn't register until it does.
  An engine's `starterKJ` is its own. Fuel Mains (`Fuel Main`, 7 cells) carry fuel where a link won't reach.

### Thrusters (the second route)

Role `thruster`: `{"force": N, "fuel": "fuel" or "electric", "burnRate", "power": kW, "spool", "exhaust": a direction, "at"}`: a
force at a point, away from its exhaust, on a body that is free and awake. Five sizes of **Jet** and **Lift Fan**, 8 kN to
200 MN. It works from its own inputs: its `throttle` wire (0 to 1) sets it; its `height` wire holds that many metres above
the ground; unwired, the driver's W, S, A and D work it by how it is mounted (`CThruster.MixForward`, `MixYaw`), and Sprint
starts the **hover hold**: every thruster pushing up the body holds the construct level at a height (Sprint raises it,
Crouch lowers it, and sinking onto something lets go). The hold stays on with nobody at the controls, so a crew can leave
the bridge. Its levels start from a share of the weight trimmed so the pushes balance about the centre of mass
(`LiftTrim`), then each thruster holds its own point as fast as the slowest spool lets it. A construct with a lit thruster
has a little drag (speed and turn). A thruster on a turntable or a hinge vectors its push.
On a planetoid the hold works along the local up and climbs faster the longer Shift is held, and high up the air thins
(docs/space/SPACE.md, "Leaving the bubble"): past the sky's edge a flyer becomes a ship in orbit.

Twelve Capital Jets hold 124,000 t at 80% and burn 6,700 l/s, an hour of 24 Ml. Twelve Capital Lift Fans at that load draw about 11 GW.

### Network (protocol 122)

A pose's fuel is a float now (the old u16 topped out at 6,553 l), a pose and a full body gain thrust and smoke bytes, and
the power flags gain 16 (the hover hold; the engine's load has the top three bits).

### Limits to know

- One engine drives a drive line (the first working one); more engines don't add up. Several plants do, and hub motors
  are how a body gets many powered bogies.
- A seat, a lever or any small part on a capital hull goes through a size adapter (the Traction City's bridge seat does).
- A bogie on a deck's side rides at the deck's height. Under a deck it bolts on by a pivot's or a hub motor's `top` socket;
  its spin decides which way its leaf points.
- Small drive parts have no `maxTorque`: a capital engine reaching one through an adapter would go through it.
- Segment contact points (`MaxSegPoints`) are shared: when big parts on joints want more than fit, each keeps its corners
  (it used to be first come, first served: a jack's last foot had none). The load between legs still isn't shared evenly.
- No ground pressure: tracks don't sink.

### Choices made (the developer may change them)

1. Hub motors are the route for many powered bogies, not one engine and a tree of shafts.
2. A thruster's fuel comes as an engine's does (touching a tank, a fuel line or a pipe); it doesn't flow through the hull.
3. Boilers and furnaces burn the fuel pool: no coal from bins yet.
4. The hover holds a world height, not a height over the ground, unless its `height` wire is used.
5. A Reactor burns nothing and doesn't explode: its cost is only its mass (and what it takes to build).
6. Drive part mass, the table at the top of the generator: a capital diesel 2,400 t, a boiler 4,800 t, a bogie 2,000 t.

## Actuator sizes and strength

Board item dev-sandbox-actuator-sizes-as01 (protocol 91). Every moving joint now comes in sizes, and has a **rating**:
the most force or torque it holds rigidly.

### The size families (`Constructs/Parts/Actuators`)

Written by `tools/constructs/actuator_parts.py` (run it from the repo root; `--check` compares), as the structural
parts are: three widths (1x1, 2x2 and 3x3 cells of the 0.25 m frame step) times three lengths (short, medium, long), 27
parts in the inventory's Joints and Motion category (sections Pistons, Motor hinges, Turntables).

| Family | Width sets | Length sets | Ratings (1x1 / 2x2 / 3x3) | Length steps |
| --- | --- | --- | --- | --- |
| Piston | push and pull force; side load it takes | stroke (and its top speed, 1 m/s per metre of stroke) | 6,000 / 24,000 / 60,000 N; side 2,000 / 8,000 / 20,000 N | 0.5 / 1 / 2 m |
| Motor hinge | torque | speed (a longer housing holds a bigger motor) | 4,000 / 12,000 / 30,000 N m | 45 / 90 / 180 deg/s |
| Turntable | torque | speed | 3,000 / 12,000 / 36,000 N m | 45 / 90 / 180 deg/s |

Mass, health, armour, idle power and fitting time grow with the size (width squared times length). The three parts that
were there before are one size each and keep their names, shapes, sockets and default settings, so blueprints and
constructs built with them still fit and behave as before: **Piston** is the 2x2 long piston (14,000 N by default, up
to 24,000), **Motor Hinge** the 1x1 medium hinge (3,000 N m, up to 4,000) and **Turntable** the 2x2 short turntable
(6,000 N m, up to 12,000). New sizes start at their full rating and speed. Part ids follow the sorted file names, so
they moved; blueprints and saves name parts, so nothing breaks.

In a part file the `joint` block takes three more keys: `give` (how far it has given at twice its rating: metres for a
piston, degrees otherwise; default a quarter of a piston's stroke, 20 degrees), `sideLoad` (a piston: N across its axis
before it gives; 0 no limit) and `overloadDamage` (hp a second at twice the rating while overloaded; 0, off, on every
shipped part).

### The configure tool

Strength (Force or Torque) and Speed run from a tenth of the size's maximum up to it, in the part's `config` schema, so
they clamp to the size's range like every setting; a lower strength caps the power it can draw (the drain is the
motor's push times its speed), and a lower speed draws less while it moves. The panel shows a **Load** bar: what the joint holds against its rating ("Load 4200 /
6000 N (70%)"), green up to the rating and red past it ("overloaded, giving way"). Overloaded actuators are tinted red
in the world, deeper the further past their rating (full at twice), pulsing a little. The load comes from the server in
the pose (a byte a joint, hundredths of the rating).

### The strength model (`ConstructPhysics.MotorPrep`)

- A moving joint's motor follows a **reference**: the target its input asks for (a turntable: its speed, or where it
  stopped when its input is 0), reached at the joint's speed.
- The motor holds the joint on the reference with at most its rating: a velocity constraint solved with the contacts
  and the other joints (the joints and the contacts on jointed constructs get 6 more solver passes than the rest, so a
  chain of actuators converges). **Up to its rating the joint is rigid**: no spring, no sag, no jitter.
- A load past the rating pushes it off the reference, and the further off it is the harder the motor pushes: rating x
  (1 + sqrt(behind / give)). It settles where that meets the load, **give x (load / rating - 1)^2** behind: a little
  just over the rating, more and more as the overload grows. Moving off the reference is damped (no bounce).
- It never asks to move faster than its speed, so when the load goes it comes back **at its speed**.
- At 4 times its rating it slips (the reference is dragged along): a hugely overloaded lift sinks to its stop.
- A piston loaded across its axis past its side load holds that much less along it (rating / (side load / its side
  rating)).
- The load counts everything: the weight it carries (riders too), the contacts on the parts beyond it (feet on the
  ground, a post in the way), inertia and other joints. A motor driving into an obstacle shows as overloaded and pushes
  at it a little past its rating (more the further its reference has run ahead, at most 4 times).
- A gunner's or sentry's aim and steering pivots keep the plain motor (at most their strength).
- An overloaded joint stays awake (it doesn't sleep giving way); one at rest within its rating sleeps as before.

### Caps and cost

A construct may have 16 joints (was 8), so a 10-joint leg or arm fits; still 128 in all. A 10-joint arm costs about
0.06 ms a tick on the cloud box; no allocation per tick.

### Self-test

"Construct actuators" (`SelfTestConstructActuators.cs`): the three families' nine sizes each (width raises the rating,
length the stroke or speed, size the mass, health and power; overload damage off); a construct on four piston legs, and
one on two-joint legs (motor hinge knees), each rated a quarter over its load and kept awake for 5 s: no sag or tilt
over 0.2 mm (it measures thousandths), then it sleeps still; a lift loaded to 1.25, 1.5 and 2 times its rating gives way
along the curve without bouncing and comes back at its speed, a client hears the load and the panel's text says so,
overload damage takes nothing while off and wears the part when on; the size in play (stroke, force, speed); the
configure values clamp to the size's range and change what the piston holds and how fast it goes; a tower of six
pistons and a ten-hinge arm worked by seeded inputs and overloaded stay finite and in one piece, the bent arm holds its
tip still, ten joints travel to a client, no allocation, and the arm's tick cost (budget 1 ms).

### Decisions made (the developer may change them)

- **Overload damage is off** (the developer's open question): `overloadDamage` is 0 on every part; set it (hp a second
  at twice the rating) to make sustained overload wear a part.
- The yield curve (give x (overload - 1)^2), the give (a quarter of a piston's stroke, 20 degrees for hinges and
  turntables) and the slip at 4 times the rating.
- The ratings, side loads, strokes and speeds in the table; a longer motor hinge or turntable is faster (not slower and
  stronger, as a gearbox would be), since the width already sets the torque.
- New sizes are plain boxes centred on their origin (not offset onto the 0.25 m lattice for even widths); the old three
  keep their shapes.
- Steering pivots have no sizes (they are mechanical, not motors).

## Large systems

What makes a mothership work: a carrier of about 300 m launching planes of about 15 m, or a traction city (a tracked land
mothership carrying a town) that lowers a drawbridge and sends its vehicles down it. The parts are written by
`tools/constructs/large_systems.py` into `Constructs/Parts/Systems` (82 files; run it again after changing a table,
`--check` compares), in the inventory's function categories, under the Medium, Large and Capital size filter (scale.py's size classes: a socket snaps only to
its own class) and, for small-class controls, the small size. Code: `ConstructDeck.cs` (decks and conveyors),
`ConstructLauncher.cs` (catapults and arrestors), `ConstructSystems.cs` (the file blocks), the dock rules in
`ConstructCargo.cs`, the winch in `ConstructLinks.cs`, the fire director in `ConstructWeapons.cs`.

**Decks (decision 30).** A raycast wheel or spring foot now stands on other constructs as it does on the map
(`ConstructPhysics.GroundRay`). The tyre works against the deck's motion at the contact (`ConstructRide.SurfaceVel`: the
body's, and its joints'), so a parked vehicle rides a moving, turning deck, a lift or a turntable with no special code,
and a driven one rolls off onto the ground at the speed it has. The deck takes the reaction (`ApplyDeckPushes`), and a
joint under the part stood on takes the weight (`Construct.DeckLoads`: it shows on the Load bar and counts against the
rating). A free body lighter than a quarter of the wheel's body isn't ground (a crate under a truck stays a hull contact),
and neither is something coupled to it. A moving joint wakes the sleeping bodies near it, since wheels hold them clear of
any contact.

| Family | Medium | Large | Capital | How it works |
| --- | --- | --- | --- | --- |
| Ramp Deck | 8x5 m | 20x20 m | 48x48 m | Hang it by its -x edge on a motor hinge. The plate sits on the middle of its cells' height, so its tip is thin; it lowers to the ground and vehicles drive down it. |
| Lift Platform | 5x5 m | 20x20 m | 48x48 m | The same deck with a pedestal: bolt it on a piston's top. |
| Hangar Door | 5x3 m | 20x12 m | 48x32 m | A panel: a piston's top on its edge socket slides it, or hang it on a motor hinge. |
| Arched Gate | 5x5 m | 20x20 m | 48x48 m | Pillars, a lintel and an arch; `jamb` sockets for hinges. |
| Gate Hinge, Gate Leaf | 2x3 m | 8x12 m | 32x32 m | A passive hinge and a door leaf: F swings it. |
| Catapult | 16 m | 32 m | 64 m | Logic kind `launch`. 25 m/s2; up to 20, 200 and 2,000 t. |
| Arrestor Gear | 16 m | 32 m | 64 m | Logic kind `arrest`. 12 m/s2; the same masses. |
| Docking Clamp | 30 t | 300 t | 3,000 t | A `dock` on a cargo volume. |
| Piston | 4, 8 m, 300 kN | 16, 32 m, 5 MN | 32, 64 m, 200 MN | Rated like the small ones (side load, give, the configure panel). |
| Motor Hinge | 500 kN m, 30 deg/s | 50 MN m, 15 | 1 GN m, 8 | The drawbridge, the crane, the gun cradle. |
| Turntable | 500 kN m, 45 deg/s | 50 MN m, 25 | 1.5 GN m, 12 | Turrets and cranes. |
| Winch | 16 m, 30 t | 32 m, 300 t | 64 m, 3,000 t | A hook with a `"winch"`. |
| Lifting Eye, Crane Boom, Conveyor | yes | yes | yes | An eye for a load; a lattice jib or gantry beam; a belt at 2, 4 and 8 m/s. |
| Shell Magazine, Ammo Hoist | 2,000 rounds, 8 m | 400, 8 m | 200, 16 m | An ammo box and a feed chute of the class. |

Also: **Flak Cannon** (Medium), **Naval Gun 20 cm** and **Rocket Battery** (Large), **Naval Gun 40 cm** (Capital), heavy
guns with their own numbers in the `gun` block; **Magnetic Lock** (Small, 6 t) and **Magnetic Lock Medium** (60 t);
**Bridge Helm**, **Crew Station**, **Fire Director**, **Long Range Sensor** (Ship systems); **Deck Speed, Fuel and Charge
Dials** (Medium); **Floodlight** (Small, Medium, Large), **Hanging Lantern** (Small, Medium); **Catwalk 4 and 8 m**, **Ladder
4 and 8 m**, **Scaffold Tower 4 m**, **Bulkhead Door Frame**, **Sliding Door** (Medium). Blueprints: **Deck Lift**, **Naval
Turret**, **Harbour Crane**, **Gate Ramp**.

**Docks.** A docking clamp or magnetic lock is a cargo volume with a `"dock"` block: `{"side": longest box side, "settle":
m/s it catches under, "maxMass": kg, "charge": kW, "refuel": l/s, "chute": true}`. So a signal or F sets it, and what it
holds rides kinematically on every machine with no pose traffic ([Cargo volumes](#cargo-volumes)). Unlike a cargo volume it
holds a crewed vehicle, needs only the body's centre of mass in its box, and has no limit a construct. While it holds
something, ten times a second, it moves charge and fuel from this construct's batteries and tanks into the held one's,
and with `"chute"` one chute's load from a bin of the held body into a bin here. Its clamps are electric: a dead battery
lets go.

**Catapults and arrestors.** The part's `"launch"` block is `{"width", "height", "accel", "maxMass"}`: its zone is its
length (its bounds along z), that wide, and that high over its top. A catapult fires on a rising input or F: the body
in the zone, moving under 3 m/s against the deck (or docked: it is let go first), is pushed along the part's -z until its
centre of mass leaves the zone. The ship takes the reaction, and an electrical ship pays 1 / 0.8 of the work from its
batteries (flat: the shot ends where it is). It puts out 1 while firing. Arrestor gear, while on (unwired: always), slows
what moves through its zone to 3 m/s against the deck. Set them flush in a deck: the rail is a cell wide, the zone is not.

**The winch** (`"hitch": {"kind": "hook", "chain": 32, "winch": {"min", "speed", "rating"}}`): F with the wrench hooks an
eye in reach, the chain starting as long as the gap; a signal of 1 reels in, -1 pays out; it stalls on a body heavier than
its rating. Protocol 118: a coupling's length is a u16 of centimetres; a reeling chain is sent every 25 cm.

**The fire director.** Wire a seat into it (else the driver's) and it into any number of guns. Those guns aim where that
seat's sitter looks (`Aim`, unwired joints only), hold fire until they bear within 8 degrees, and fire on the sitter's fire
key (the director's output is the trigger, one tick late like every wire).

**The helm.** `"seat": {"helm": true}`: the driver's seat before any other, wherever it comes in the part list.

**Conveyors.** Logic kind `conveyor` with `"conveyor": {"speed", "dir"}`: a signal sets its speed (-1 to 1), or unwired F
runs it. Hull contacts and wheels on it get its surface speed. It doesn't carry players.

**A lift, a ramp and a gate** go together on any deck: a piston under a Lift Platform; a Motor Hinge on a deck's edge
socket with a Ramp Deck on its leaf, axis level (the Gate Ramp blueprint), its Lowest angle set for how high the deck is.
Keep ramps under about 15 degrees for a buggy: its nose grounds on anything steeper.

**Self-test**: "Large systems" and "Large systems network" (`Source/App/SelfTestLargeSystems.cs`).

**Choices made (the developer may change them)**: wheels stand on a free body of at least a quarter of their body's mass;
the plate of a deck sits mid-cell, with sockets on its edges and four on top at the plate's face (off the lattice by the
plate's half thickness); catapults push the body directly, with no shuttle; a catapult's and arrestor's reaction goes on
the body, not through a joint; the arrestor needs no hook; docks feed one way, and unload bins with no filter; a winch has
no chain tension limit past its static rating; a conveyor is one logic part with no belt animation; a director's guns only
hold fire on unwired joints' aim. The blueprints carry no wires or small controls: fit a switch through a size-class adapter, or use F on a part (a piston or hinge with nothing wired into it opens and shuts on F).

## Flexible joints

The user's request: "there needs to be a flexible joint connection for items", which turned out to mean both a joint part
with free play (here) and a chute that survives one ([Flexible chutes](#flexible-chutes)). Code: `Source/Constructs/ConstructFlex.cs`.
Data: `Constructs/Parts/Actuators/FlexJoints` (written by `tools/constructs/actuator_parts.py`, then `make_joint_sides.py`).

A small passive part. What hangs on its leaf bends any way up to a cone and springs back; there is no motor and it draws no
power. Six parts in the inventory's Joints and Motion category, section Flexible joints:

| Part | Bends | Twists | Stiffness (1x1 / 2x2 / 3x3, N m a degree) | Rating (N m) | Joints it takes |
| --- | --- | --- | --- | --- | --- |
| Flex Joint 1x1, 2x2, 3x3 (rubber) | 25 deg | no | 80 / 320 / 960 | 2,000 / 8,000 / 24,000 | 2 |
| Ball Joint 1x1, 2x2, 3x3 | 50 deg | 30 deg each way | 16 / 64 / 192 | 2,000 / 8,000 / 24,000 | 3 |

In a part file the `joint` block has `"type": "flex"`, `axis` (out towards the leaf: what it twists about), `pivot`, `leaf`,
`cone` (degrees, 2 to 80), `twist` (degrees, 0 for none), `stiffness` (N m a degree), `damping` (a ratio, default 0.7),
`strength` (the rating) and `overloadDamage`. The configure tool sets the Bend limit (up to the part's cone), Stiffness and
Damping; the Load bar shows what it holds against its rating.

**It is not a third kind of joint.** A flexible joint is two or three ordinary one-number joints in a row at one pivot, all
naming the same part (`JointType.Flex`): a swing about each axis square to the leaf, then a twist about the leaf. The
segments between them carry no parts, and the leaf's parts sit on the last (`Construct.LeafSegOf`; `JointOf` finds the first,
so `JointSides` and anything counting "the parts under it" just work). So every system that walks a joint chain needed
nothing new: `SegPose`, `MoveSeg`, the contact and coupling rows, recoil, `SegPointVel` (riders), drawing, the pose on the
wire (angle, speed and load per joint), blueprints (a step on the leaf makes the joints), splits and joins. The pieces can
never drift apart, as for any joint. What it costs is slots: **a rubber joint takes 2 of a construct's 255 joints and a ball
joint 3**, and the refusal says so (`ConstructPartDef.JointSlots`, `ConstructBuild.LeafRefusal`). The gimbal's flip at 90
degrees is out of reach, since a cone is at most 80.

**Physics** (`ConstructPhysics.FlexPrep`, a row in `JointRows`). The spring is a soft constraint solved with the contacts,
implicit, so no stiffness can blow it up. The damping is a ratio of what critically damps this tick's load, so it settles
alike under a rock or a player. It settles at load / stiffness. The cone is a circle, not a square: each swing may reach what
the other leaves (`FlexReach`), and what passes the circle is put back on it (`FlexAfter`). The stops are the ordinary limit
rows: **rigid**, so a load past its rating sits on the stop and shows overloaded (`Over`, the red tint), with
`overloadDamage` (off) to make that wear it. Its reaction goes to the body like a motor's.

- A wired signal locks it where it is, as for a passive hinge. F says what it does and refuses (it has no latch).
- Drive shafts can't cross it (`"a drive shaft can't cross a joint"`), and a U joint against it says so. A chute can.
- Riders on the leaf count towards its load (their weight, at the carried mass's centre, not their feet).
- **Wheels beyond it don't roll.** A wheel is a raycast wheel only on the body or a steering pivot on the body
  (`Construct.IsRaycastWheel`): beyond a flexible joint, as beyond a hinge, it is a part that touches the ground. Its force
  would have to go through the joint chain; that is future work with the collision rework.
- Not modelled, as for every joint: the body's acceleration doesn't swing what hangs on the joint (a flexible arm on a
  braking truck doesn't lurch), only gravity, contacts, couplings and recoil do. The leaf's half of the part is drawn
  bending, but the part collides as one box on its base side.

Decisions the developer may change: the sizes and numbers above; `stiffness` x `cone` = rating on a rubber joint (it reaches its
stop at its rating), and 0.4 of that on a ball joint; a joint's budget (raise `Construct.MaxJoints` if players want long
flexible chains: every array scales with it, but each construct pays for it); overload stays off, as on the actuators.

### Flexible chutes

What happened before: a chute could already be routed across a joint (only shafts are refused), checked in the rest pose only.
Moving, it was drawn as one straight box between its two ends, however far they went (through the parts, stretched without
limit, right round a turntable), and went on carrying, with no limit and no reason. The bend rule and the clipping rule were
never looked at again. Now (`Source/Constructs/ConstructFlexChute.cs`):

- A chute segment whose two ends are on different moving segments (`Construct.MoveSeg`, so a freeform weld doesn't count) is a
  **flexible section** by itself: no new part, nothing to pick. It is drawn as a ribbed bellows curving between its ends
  (`RouteRender.Bellows`), and is free of the duct's 45 degree steps and of clipping, since where it runs at rest means
  little. It carries bulk and parts as any chute does.
- **Its limit**: it stretches to 3 times its rest length (at least 0.5 m more), folds to 0.3 of it, and its ends turn 135
  degrees apart. The bellows goes from the duct's colour to red over the last 30%, and the look line reads "flexible section
  1.4 of 3.0 m, bent 40 of 135 deg". **Past it the chute stops** ("STOPPED, stretched too far (bring the joint back)") and
  carries on when the joint comes back. **Past 1.25 times its limit it tears**: the segment is cut as if shot, a hint says
  so, and the wrench mends it. A branch stops with the link it comes off.
- So a long-stroke piston needs a long rest section (a node on each side of the joint sets it), and a chute over a free
  turntable stops at 135 degrees and tears at 169: use a chain of them, or put the store on the axis.
- Nothing new on the wire: both peers work a section's strain out from the joints, and a tear is the link's health.
- Only chutes flex like this; cables and pipes across a joint are drawn straight as before, with no limit.

**Between constructs: the cargo hose.** Hoses carried no items, and a world link snaps when a free construct moves. Two new
hose parts (`Constructs/Parts/Hoses`, `"carries": "items"`): the **Cargo Hose Reel** (15 m, a load a second, chute input "Cargo
in") and the **Cargo Hose Inlet** (chute output "Cargo out"). Run a chute from a store into the reel on one construct, and from the
inlet to a store on the other; F takes the coupling and F plugs it in, as for any hose. What moves goes straight from the
store feeding the reel to the store the inlet feeds (`ConstructRoutes.HoseMove`): bulk or parts, chute rules as ever, and past 15 m
it pops out unhurt and reels back, so a docked vehicle can drive or fly off. It is a person's job, one way (reel to inlet;
fit a pair for both ways) and stops at no fixed dock: **no automatic coupling yet** (a docking port that plugs itself would be the next
step for the mothership loop). A plugged cargo hose joins the two sides' beacon networks.

## Routed links (decision 20)

Board item dev-sandbox-routed-links-rl02 (the developer's design, 2026-10-30). Code: `Source/Constructs/ConstructRoutes.cs`
(the links, their rules, flow, damage, the wire format), `ConstructRoutesBuild.cs` (blueprints' `"links"`, the migration),
`ConstructEditsRoutes.cs` (the server's edits), `Source/App/Program.SandboxLinks.cs` (the linking tool on this PC),
`Source/UI/SandboxLinkWheel.cs` (the linking tool's options on the tool wheel's outer ring), `Source/Render/RouteRender.cs` (drawing them).

One routed-link system on the linking tool replaces the invisible wires and the Fuel Line part. A link is a polyline of
**nodes** from an **output** to an **input** on one construct, routed node by node over its surface, drawn by one
segment piece and one elbow piece per type (instanced), and simulated as a graph edge (never a physics body).

| Type | Look | Routing rule | Carries | Segment health |
| --- | --- | --- | --- | --- |
| Data | small flat cable | free | on/off and number signals (the logic's wires) | 20 |
| Power | larger flat cable, red stripe | free | electricity from a battery or alternator to anything that draws it | 40 |
| Pipe | round, a flange ring at each node, one neutral look | bends in 15 degree steps | fluids from tanks to engines | 80 |
| Drive shaft | round steel, a U joint at each bend | straight, at most 45 degrees a bend (and into a port), no corners (a gearbox does that), no bare junctions (a T gearbox does that), never across a joint (a U or CV joint device does that) | torque from engines, gearboxes, transfer parts and differentials, port to port, and into a Drill Module's Drive in | 250 |
| Chute | square duct with rims, a bellows across a moving joint ([Flexible chutes](#flexible-chutes)) | bends in 45 degree steps | items between storage parts (a load of bulk or one stored part a second); a part may name its own chute ports (a Storage Volume's Chute in and Chute out, a Drill Module's Chute out, which takes nothing in) | 80 |

**One pipe** (decision 28): there is a single pipe and it carries any fluid (fuel, water, oil, steam...). There are no pipe
styles, no per-style radius or ratings (every pipe is 5 cm in radius) and no wrong-pipe failures: nothing weeps, burns,
bursts or makes an engine sputter because of what runs through it. It is drawn one neutral way, round with a flange ring
at each end of a section, so at every node (its colour is for the paint tool to change). Pipes still carry what the
source tank holds to the engines it feeds; a cut pipe still pours fuel out. Blueprints and saves from before the change
may still say `"style": "fuel"` (or water, oil, steam) on a pipe link: it is read and ignored, and the link loads as the
one pipe. Blueprints no longer write it. On the wire the type byte of a link is the type alone (protocol 98); the high
nibble that held the style is masked off when read.

**Routing** (the linking tool): Fire on a device (a picker of ALL its ports, every type, by name: "Fuel in", "Drive out"; the port picked gives the link's type; a one-port device gets a one-option picker too; [Link type from the port](#link-type-from-the-port-at01)); each
Fire places a node on the surface under the crosshair: off the surface by the link's radius, on the construct's 5 cm grid
along the surface, onto the straight line on from the last segment when close to it, lined up with a neighbouring link's
nodes, then turned to the type's angle rule. The preview shows the link so far and the next segment, red with the reason
when a rule is broken (too close, a node sinking into a part, a segment through a solid part, the bend, the reach: at
most 8 m a segment, 24 m a link, 32 nodes). Fire on an input finishes it, by the route the game found ([Auto-routing](#routed-links-decision-20), below; a picker when it has several free ports, with the port nearest the crosshair highlighted so one more click confirms); Aim or Backspace takes
the last node off; Esc cancels. A straight data cable may still go into **another** construct's input (milestone 3's wire
between constructs). The server checks the whole link again (`ConstructRoutes.Check`) from where the player stands.

**Clipping grace and embedding** (board item dev-sandbox-link-clip-grace-lg01; `ConstructRoutes.PathRule`, the one rule
the server's `Check` and the client's preview both run). Along the first and last stretch of a link nothing clips it:
for `Grace(radius)` = 2 x the link's diameter + 10 cm (`GraceDiameters`, `GraceExtra`) from where it leaves its output
(or from its junction) and up to where it enters its input, nodes and segments ignore every part (the rail or frame a
device sits on too). The grace counts from where the link leaves the device's own boxes (`ExitAlong`; a port at a part's
centre would otherwise use it up inside the part), and the device itself stays exempt on its end segment as before.
Past the grace a link may sink up to `EmbedFraction` (60%) of its thickness into a part: its centreline may be
(2 x 0.6 - 1) = 0.2 x radius inside a surface (`EmbedDepth`). Nodes use a signed distance to the boxes
(`SignedDistance`, `NodeSinks`); segments test the centreline against each box shrunk by that depth (`SegmentBlocked`),
which is what lets a link turn round the outside of a corner. Nodes are still snapped onto the surface (decision 20).

**Hold to adjust** (lg01): when the preview is red, LMB doesn't drop the node: it freezes at the click and the adjust
mode starts. The game first searches along the surface normal (out 1 cm at a time up to 50 cm, then in down to the
embedding limit: `AdjustSearch`) and jumps to the nearest valid offset; while LMB is held the wheel moves it along that
normal (1 cm a notch, Ctrl 5 cm; not Shift, which is free routing) and the preview re-checks every step, red with the
reason or green. Letting go on green places the node; on red nothing is placed (RMB cancels too). The offset shows in cm
beside the crosshair, and the wheel neither swaps weapons nor turns the wheels meanwhile. The same works for the input
end (the last node before the input moves along the normal it was placed on, then the link finishes) and for the edit
mode's move node, whose preview is now checked with the server's rule (`CheckMove`) and goes red too. The node rules are
reusable `INodeRule`s (`OpenRule`, `ClosedRule`, `MoveRule`); nothing new goes on the wire.

**Free routing** (board item dev-sandbox-link-shift-free-ls01; `ConstructRoutes.ClickEnds`): while the **Free routing**
key is held (Left Shift by default, rebindable; it shares the key with Sprint, see `Keybinds.Overlap`), aiming at any
device places an ordinary node on its surface (the normal snap, grid, angle, grace and embedding rules) and never ends
the link, opens the port picker or branches, so a link can run along a device with a matching port or beside another
link. The device's highlight is hidden while it is held; the HUD line says "Left Shift: free routing (no port snap)".
Let go of it to end at an input. Before the first click it does nothing special. It keeps working while adjusting a
node, and a moved node may sit on a device either way. The server needs nothing new: a node on a device is a legal node.

**Auto-routing** (`Source/Constructs/ConstructAutoRoute.cs`, `RouteSearch`; `Source/App/Program.SandboxAutoRoute.cs`, the tool). With
an output picked, aiming at an input shows the whole route as a live ghost: blue while it is searched for, green when it fits,
red with the reason. One click confirms it (an input with one free port); an input with several ports opens the picker as
before, its ghost follows the port the pointer is on and a click there confirms. A click on a surface still drops a node, and
the router carries on from that node, so nodes clicked on the way are waypoints; aimed at a surface the straight line can't reach,
the ghost routes to that spot, and the click takes the route with the spot as its last node. Hold the Free routing key for the old
manual flow: straight segments only, no search. Hold to adjust is unchanged, and acts when no route was found. The look line says where it
stands in the usual form: `Pipe to Engine: route found, 5 nodes, 6.2 m - LMB: confirm`,
`Pipe to Engine: no route - no way round the parts - LMB a surface: place a node`.

- **The straight line comes first.** A straight link that fits is used as it is when it is a shaft's, under 75 cm, or square with the
  construct's axes. A longer diagonal goes to the router for a tidier square route, and comes back as the straight line
  only if no square route exists.
- **Cables, pipes and chutes** search a lattice of box faces: the planes one link radius off each box, the two ends' planes and a stub off
  them (a Hanan grid: the shortest square route round axis-aligned boxes lies on it), thinned to at least 5 cm apart. Edges a box
  touches are marked in a grid, which is what keeps A* cheap: looking up an edge costs one array read. A* runs over (point, direction),
  charging 0.4 m a bend, 0.8 x the length along a surface or beside another link's run, and 2.5 x along another link's run. All
  turns are 90 degrees, which suit the pipe's 15 and the chute's 45 degree steps, and a run is at least 10 cm between
  bends (1.5 x the radius for a chute). A link leaves and arrives along a port's face (sideways costs 60 cm, backwards never), and the first and last
  stretch may pass through what the clipping grace forgives. Four stages widen the look: 1 m round the ends, 4 m, 12 m,
  then 12 m on a 2 cm lattice with the clearance dropped to nothing, centreline on the surface, which `EmbedDepth` allows; that is for slots narrower than a link.
- **A flood from the goal** first marks every lattice point that can reach it, whatever the bends, so a shut-in goal costs one pass of
  the lattice, and A* stays out of pockets with no way on.
- **Shafts** can't turn 90 degrees, so they search an even lattice (10 cm, growing with the distance) in the 18 directions a 45 degree
  limit can step between, finishing as soon as a straight run reaches the port, or a stub off it. More than 4 bends is refused
  (`ShaftMaxBends`), and the reason names what is needed: `no gentle shaft run: fit a right-angle gearbox at the corner` when the
  ports are more than 80 degrees apart, else `no clear shaft run: line the ports up, or fit a gearbox or a U joint on the way`.
- **Joints and constructs.** A shaft across a joint gets the old refusal at once. Other links may cross: nodes go on the output's side until
  a node is nearer the input's side, so a chute crosses once, in one flexible section. Off the construct, a data cable into
  another construct and world links behave as they did: the router only runs on one construct.
- **Nodes** are ordinary nodes: each goes on the nearest part on its side of the joint, in the path's frame (`Hosts`). Pieces freeform-welded at an
  angle count as their bounds. The client checks the result with `ConstructRoutes.Check`, and the server checks it as it checks a manual
  link: nothing new is trusted and nothing new is sent (no wire change). The server's reach rule stands: a link is only taken from
  within 4.5 m of both its ends, and the ghost says so in red before the click.
- **Cost.** It runs on the client, 2 ms a frame (`BudgetMs`), and nothing allocates after its first use makes its tables (about 19 MB, once). A search restarts
  when the construct is rebuilt (`RebuildStamp` is in the key), and 8 results are kept per link and goal, so sweeping between ports searches nothing twice.
  Caps: 1,048,576 lattice points, 262,144 states, 200,000 states searched per stage. Waypoints wait for the crosshair
  to rest for 150 ms. Measured on the 2-core box under load, on random 30% full block hulls (shared ports, links up to 24 m):
  a 2,000-part hull found routes in a median of 1 to 4 frames (shafts 12 to 16) and a 7,990-part hull in 2 to 8, worst 254 frames (4 s) for
  a failing search; the final `Check` takes 0.02 ms on 7,990 parts, because `Blocked` and `NodeSinks` now ask the part tree when it can answer.
- **Limits.** Routes are square, so a pipe or chute tail left at an odd angle by a manual node may have no legal way on (the look line says so:
  take the node off or carry on by hand). Links aren't obstacles to each other, only a cost. A search that gives up says so, and manual nodes
  still work.

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

**Edit modes** (options on the tool wheel's outer ring round the linking tool): delete a link (and its branches), move a node, re-route a link, highlight
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

### Named link ports (np01)

Board item dev-link-named-ports-np01 (the developer, 2026-10-30 / 31). A part file declares the ports its links use **by
function**, with a top-level `"ports"` list (`Source/Constructs/ConstructPorts.cs`): `name` ("Fuel in", "Drive out",
"Input shaft"), `type` (`shaft`, `pipe`, `power`, `chute`; data ports stay the logic part's `DataIn` / `DataOut`), `dir`
(`in`, `out`, `both`), and where it is: a shaft port names its `socket` (that socket's index is still the link's port byte, so
the drive solver and the wire format are unchanged) and may move its point (`at`) and facing (`face`) to the middle of a
face; a pipe or power port gives its own `at` + `face`. A pipe port may name its `fluid` (fuel, coolant, exhaust, oil,
water) and any port may say `"noEffect": true` (listed in the picker as "(no effect yet)": the exhaust and coolant ports,
a tank's Fill, a battery's Power in; the simulation has no exhaust or cooling system yet and does nothing with them, and a
tank's pipe into an engine's Coolant in does not feed it). The first port of a type on a part is port byte 255, the next ones
`0x80 | k` (shafts use the socket index). Every part that draws power gets an implicit "Power in".

- **What a link starts and ends at.** `ConstructRoutes.IsOutput` / `IsInput` for shafts, pipes and power cables are "the part
  declares a port of that type that can start (end) a link"; a part without one is red with "the X has no shaft port"
  (structural parts never offer shaft ports). A link starts and ends exactly at the declared point of its port
  (`PortPoint` / `PortDir`): a differential's "Input shaft" is the middle of its -Z face, its "Output left" / "Output right"
  the middle of the side faces; each port has a small flange or stub drawn on the face. A shaft leaves a port straight out.
- **The picker** lists only the ports of the link's type that can take it and are free, by name, with a hint: in / out, the
  fluid, "(no effect yet)". A pipe carries what its output port produces, so ending one on an engine lists just the inlets
  that want that fluid ("Fuel in"); with a single candidate it connects with no picker. While aiming at a part, its matching
  ports show as small labelled markers where they sit.
- **Fluid warning.** An input port that wants another fluid than the pipe carries (exhaust into a fuel inlet) is allowed but
  the link's look line says "WARNING: exhaust into a fuel inlet" (`ConstructRoutes.FluidWarning`, `EndNote`).
- **Old saves.** A blueprint link whose `fromPort` / `toPort` is a socket's name (the old directional ports: "+z", "-z0")
  loads onto the nearest named port of the same type that fits (`TryPortByName`, `NearestPort`); with none the link is
  dropped and logged ("a drive shaft at the X's old port ... dropped"). The old drive-shaft parts migrate the same way.
  Blueprints now write the port's name. Protocol 103.
- **Checks.** `--check-construct-parts` prints every part's ports and flags a part with a linking role missing the ports it
  needs (`ConstructParts.PortProblems`); the loader refuses a port whose point is not on the part's surface. Self-test:
  **Named link ports** (`SelfTestNamedPorts.cs`).

### Link type from the port (at01)

Board item dev-link-auto-type-at01 (the developer, 2026-10-31; decision 20 amended). The linking tool has **no type choice**:
its outer ring holds only the modes (`SandboxLinkWheel`: Route links, Delete links, Move node, Re-route, Highlight all). The
type comes from the port.

- **Start.** Fire on a device opens the **port picker** with ALL of its ports at once (`ConstructRoutes.AllPorts`: every type,
  inputs and outputs, named ports then data in / out, then a storage part's chute), each by name with its type icon and colour
  (`SandboxLinkWheel.TypeColour`) and a hint line (type, in / out, the fluid, "(no effect yet)"). Ports a link can't start at
  (inputs, shaft sockets in use) are greyed with the reason. The usable port nearest the crosshair is **pre-highlighted**
  (`RadialMenu.Preselect`), so a click confirms it with no further aim; a one-port device (a seat's Data out) gets a
  one-option picker. Picking a port starts a link of **that port's type** with that type's rules, look and preview. While
  aiming at a device, every port is a labelled marker on the part in its type's colour (`Program.LinkPortMarkers`).
- **End.** While routing, a click on a device whose ports include one that takes the link opens its picker: the ports of the
  link's type that are compatible are enabled and the nearest is highlighted; every other port is greyed with its reason
  (`ConstructRoutes.EndWhy`: "a drive shaft can't connect to Fuel in", "Drive out is an output: a pipe ends at an input",
  "this pipe carries fuel, Coolant in takes coolant"). A device with no port of the link's type stays a surface for a node,
  and its markers are grey with the reason. The picker is always shown, even for a single matching port (np01's amendment).
  The picked port is checked with the server's rule (`ConstructRoutes.Check`) and a refusal is said, with the link kept.
- **Branching** off a link takes the link's type. Free routing (Shift), hold-and-scroll, world links and hoses are as before.
- **HUD.** The line names the link being made: "Drive shaft from Engine: Drive out, 2 nodes: ...".
- **Wire.** Nothing changes (a link already records its type); protocol stays 104.
- **Self-test.** **Link auto type** (`SelfTestLinkAutoType.cs`), through `Program.LinkTestFrame` (the linking tool's real per-frame
  input) with a host and a separate client: the ring has no type; an engine lists Fuel in, Drive out and Exhaust out together
  (a fuel inlet greyed at the start); a transmission's Output shaft starts a drive shaft that ends on the differential's
  Input shaft (output shafts greyed) by one confirming click; a tank's Fuel out starts a pipe ending on Fuel in (Drive out greyed
  "a pipe can't connect to Drive out"); a fuel inlet's reason for a shaft; branching off a pipe makes a pipe; a seat's one-option
  picker; a lamp's end picker (Power in greyed for a data cable); delete and re-route; the client mirrors the links.

### Self-test

The **Construct routed links** section (`Source/App/SelfTestConstructRoutes.cs`): each type's bend rule and the snap
keeping it, nodes snapped off a surface never inside it, a sunk node, a segment through a part and wrong ends refused;
a switch lights two lamps through a data junction, a cut branch and its repair; a power cable carrying the circuit past a
wrecked tank, its junction splitting evenly, cut and sparking; fuel reaching two engines through a pipe junction, a cut
branch starving one and leaking; the buggy's fuel pipe
cut and repaired; the migrated truck's shafts driving four wheels (two with the front shaft cut), a bare shaft junction
and a shaft corner refused; a chute junction sharing a bin's metal evenly; links following a driving buggy and a turning turret;
blueprint `"links"` and `CBody` round trips (a short packet refused); the server's linking tool checks; the migrations;
300 part files with their own ids; no allocation in 120 ticks of three constructs with links, nor in rays at a cable.

**Link clipping grace** (`Source/App/SelfTestLinkRouting.cs`): a pipe from a tank to an engine on the slab's edge,
its end segments rising from below the slab's side over its corner, passes within the grace where the old rule refused
it, and a segment through the slab past the grace is still refused; a pipe's nodes 50% sunk into the slab pass,
70% fail; an outside turn over a frame block's edge passes (the old rule refused it); a node clicked into a wall: the
adjust search finds +1 cm, one notch moves 1 cm and Ctrl 5 cm, the range is clamped; the preview and the server agree.
**Link free routing**: with free routing a click on an engine (an input of pipes) places a node there and the link stays
open, without it the click ends the link; a pipe routed along that engine to another is accepted and feeds only the
other; the key is Left Shift beside Sprint without a clash.

### Choices made (the developer may change them)

1. Tunable constants in `ConstructRoutes`: pipe 15 degree steps, chute 45 degree steps (developer), shaft at most 45
   degrees a bend (developer), shaft junctions need a T gearbox (developer); 5 cm node grid; 8 m a segment, 24 m a link,
   32 nodes; segment health 20/40/80/250/80; a cut fuel pipe leaks 0.5 l/s; a chute moves a load (10 bulk) or one part a second.
2. Picking a link type: none any more (board item dev-link-auto-type-at01): the port clicked gives it. The outer ring keeps the modes.
3. Power cables add to the frame circuit (the frame still carries power, so every earlier construct works); a cable
   carries the circuit both ways.
4. A shaft link is a drive-line edge with no ratio (each U joint keeps 99%); chain and belt drives stay devices.
5. A link with no nodes is checked like any other (it may not pass through a part); migrated wires and blueprint links
   with no nodes are straight and checked for reach only.
6. Repairs are instant with the wrench or the welder; junction ratios are even (the configure tool can make them
   settable later).
7. (lg01, ls01) The grace counts from where a link leaves its device's boxes, not from the part's centre; the adjust
   search goes out first, then in; the input end's adjust moves the last node (a link with no nodes can't be adjusted:
   its reason is said); the adjust's big step is Ctrl (which is also Crouch: holding it crouches); Free routing shares
   Left Shift with Sprint (holding it while routing also sprints).

## World links (board item wl01)

A routed link may leave its construct: a base can be laid out on the ground with pipes, cables and chutes running across
the terrain between constructs. The code is `Source/Constructs/WorldLinks.cs` (the list and its tick),
`ConstructRoutesWorld.cs` (the rules), `ConstructEditsWorldLinks.cs` (the server's checks), `Game/GameSandboxWorldLinks.cs`
(actions, the network, hints) and `App/Program.SandboxWorldLinks.cs` (the linking tool).

- **What it is.** A server-owned list beside the constructs (`ConstructWorld.WorldLinks`, at most 64, 1024 nodes). Each
  end is a construct's part (named by its tag, resolved each tick like the wires between constructs); each node is either
  on the world (a world position on the terrain or static map geometry: map boxes, buildings, props) or on a construct's
  part (the rest frame of that part's segment, like an ordinary link's node).
- **Routing.** With the linking tool, start a link at an output as usual; the first click off the construct (on the ground,
  a wall or another construct) makes it a world link. Each LMB places a node on whatever is under the crosshair, snapped
  off the surface by the link's radius (on the ground, the height is found again under the snapped spot), on the 5 cm
  grid and to the type's angle rule (`ConstructRoutes.WorldSnap`, sharing `AngleSnap` with the construct links). A red
  node moves to the nearest valid spot along its surface normal when there is one (the shared `AdjustSearch` with
  `WorldOpenRule`). LMB on a matching input of any construct finishes it; RMB or Backspace takes the last node off; Esc
  cancels. A data cable clicked straight into another construct's input with no nodes is still milestone 3's straight
  wire. The delete mode deletes a world link; its look line names its ends.
- **The rules** (`ConstructRoutes.WorldPathRule`, one rule for the server and the preview): the construct links' segment
  and length limits (8 m, 24 m), angle rules, clipping grace at the ends and embedding forgiveness, checked against every
  construct near the path (in its rest frame) and against the static world by rays (a segment lifted by the embedding
  depth must not hit anything; a world node must not sink deeper than it). A world node must sit on a surface (within its
  radius plus 15 cm). The end must be in reach of the player; the start within the link's reach.
- **What flows.** Fuel from a tank into another construct's tank (2 l/s, while it has room); charge from a battery or
  alternator into another construct's batteries (5 kW); signals (the logic reads the source's value like a wire between
  constructs); items from storage to storage (a chute's move a second). Anchored constructs count as the world.
- **Moving constructs.** A link with any point on a free (not anchored) construct snaps when that point moves more than
  5 cm or its construct turns more than 3 degrees since the link was made (`SnapMetres`, `SnapDegrees`): it is removed,
  the end sparks (a power cable) or spills (a pipe) (`CFireKind.LinkSnap`), and the players within 25 m get the hint
  "the power cable to the vehicle tore off, sparking: it moved". Nothing is damaged. Making a link doesn't wake a free
  construct (a parked buggy woken on flat ground creeps about 5 cm before it sleeps again, which would snap it at once).
- **Ground going.** Each tick one world node checks its surface is still there (a ray the way it found it when made); if
  it is gone the link breaks ("the ground under it is gone"). Blasts now dig craters (decision 16,
  docs/sandbox/TERRAIN-DIGGING.md); the self-test stands in a world that loses its ground.

### Hoses (temporary connections)

Six parts in `Constructs/Parts/Hoses` (role `"hose"`, a `"hose"` block: `end` outlet or inlet, `carries` fluid,
power or data, `at` where the hose leaves or plugs in, and on an outlet `length` and `flow`): the Fluid Hose Reel (12 m,
2 l/s), Power Lead Reel (10 m, 6 kW) and Data Lead Reel (8 m), and their inlets (Fluid Hose Inlet, Power Lead Socket, Data
Lead Socket). The fluid hose replaced the fuel and water hoses (decision 28); the old names "Fuel Hose Reel", "Water Hose
Reel" and the two inlets still load as the fluid hose (`ConstructParts.Renamed`), and `"carries": "fuel"` or `"water"` in a
part file reads as `"fluid"`. The inventory lists them under Joints, "Hoses and leads".

- F with empty hands (any tool) on a reel takes the receiver (a nozzle or plug); it is held while the hose is out, not a
  backpack item, and counts as full hands for `ConstructEdits.Give`. F on a matching inlet of another construct within
  the hose's length plugs it in; F on the plugged inlet takes it out into the hands; F on its reel hangs it back; the
  Drop key lets go (it reels back), as does dying or leaving.
- It connects any two constructs, moving or not: pulled past its length (a vehicle driving off) a plugged hose pops out,
  and a held one slips from the hands 25 cm past it; it reels back, nothing is damaged, and the players near get a hint.
- What flows goes from the reel's construct into the inlet's at the reel's rated flow: fluid (between tanks that hold the same
  fluid: fuel into fuel tanks, water into water tanks), charge into the batteries, and a data lead's signal (wired into the reel, it comes out of
  the socket).
- A hose is drawn as a sagging rope (a parabola from its slack, 12 thin boxes) from the reel to the receiver in the
  holder's hands or the inlet; nothing is simulated or collides. The look line on a reel or an inlet says what F does
  there; holding one, the HUD says how much hose is out.

### Network and state (protocol 95)

`CBodyKind.WorldLinks` carries the whole list (each link: type, ends' construct ids and parts, nodes: construct id, 0
for the world, part, position; each hose off its reel: the reel, the holder, the inlet) whenever it changes, and to a
joiner. `CAct.WorldRoute` and `CAct.WorldRouteEdit` (delete) are at the end of `CActKind`; hoses use `CAct.Use` on their
parts (the server sees the role) and `CAct.Drop`. `CFireKind.LinkSnap` draws a snap's sparks or spill.

**What's missing:** world saving doesn't exist yet, so world links and hoses live as long as the match (they round-trip
through the join snapshot); blueprints don't carry them (they are between constructs). World links take no damage yet
(construct links do), and they carry whatever the tanks hold (fuel or water) through the same pipe.

### Self-test

"World links" (`SelfTestWorldLinks.cs`): a pipe across the ground between two pinned tanks carries 2 l of fuel in a second (and the same pipe with the tanks holding water, 2 l of water),
and a sunk node, a wrong bend and a shaft are refused; a data cable across the ground lights a lamp on another construct,
and a node in the air is refused; a power cable from a buggy still settling to a pinned battery stays while it comes to
rest, charges it, and snaps within a tick of the buggy driving off with the hint and sparks; the ground going breaks a
link; a fluid hose from a station refuels the buggy at its rated flow, unplugs and hangs back by hand, and pops out at its
length when the buggy drives off, unhurt; the wire round trip; no allocation per tick; a real host and a separate client
on the Construct Yard: the client routes a world link and plugs a hose over the network, and it and a late joiner mirror them.

### Choices made (the developer may change them)

- A drive shaft can't be a world link: torque stays on one body (two bodies' drive lines would need joining).
- A world pipe ends at a tank, not an engine: the engine runs from its own tank (one fuel pool per construct).
- The hold-to-adjust mode is for construct links; a red world node jumps to the nearest valid spot along its normal.
- Links keep the construct links' limits (8 m segments, 24 m in all); hoses pick up with E only (not LMB).
- Hoses go from the reel's construct to the inlet's (a station refuels, a base charges); a reel is a station's or a
  vehicle's alike.

## The paint tool (board item pp01)

Decision 29. A spray can on the tool wheel's north-west segment (it took the hand drill's place: the wheel keeps eight
segments and the compass-point flicks of the other tools stay where they were). With it out, **LMB** paints the part (or
routed link: a pipe, cable, shaft or chute) under the crosshair; **hold LMB and sweep** to paint every part the crosshair
passes over, each once per stroke (`PaintStroke`: a set of what is done, cleared when the button goes up, so a click
paints one and a part is never acted on twice); **RMB** is the eyedropper (it reads this PC's copy of the part: its paint,
else its own main colour; nothing is sent) and puts the colour in the can.

**The ring round the tool** (`SandboxPaintWheel`, an `IToolOptions`): the modes **Part** (the default), **Whole
construct** (every part and link of the aimed construct; in survival the first click asks for a second within four
seconds) and **Reset colour** (back to the part's or link's own colours); ten swatches (orange, black, white, grey, rust,
olive, sand, red, blue, yellow); **Custom...**, which frees the mouse and opens a small ImGui picker (hue, saturation and
value, the recent colours, Use / Close; Esc closes it); and **Recent colours**, whose third-tier arc holds the last eight
colours used (moved to the front when used again). Letting go on the tool itself keeps the last mode and colour. The
recent colours are kept for the session only (nothing is saved).

**What takes paint.** A part's colour is `CPart.Paint` and a link's `CRoute.Paint`: a `uint`, 0 for "own colours", else
`0x01000000 | 0xRRGGBB` (`ConstructPaint`). It moves with its part or link when the arrays close up, split or join. A part
file's box says `"paintable"`: by default every box is, except **glass** and a **wheel's tyre** (a wheel's solid boxes;
its rim details take paint); the Lamp's lens says `"paintable": false`. A painted part's paintable boxes are drawn in
the paint times their **tone** (the box's brightness against the part's main colour, kept between 0.65 and 1.3), so a
hinge's dark pin stays darker than its plate; the other boxes keep their colours. Frozen and overloaded tints, damage
shading and a lit lamp's glow work as before. A painted link's segments, elbows and caps are drawn in the colour (a
pipe's bands keep theirs). Cargo volume outlines and stored items are not painted. **World links are not paintable yet.**

**Free and instant** in both modes; the server (`ConstructEdits.Paint`) checks the paint tool is out, no seat, the reach
and decision 19's contested-construct rule (`Contested`: survival, an enemy construct while its owner is alive nearby).
A part with nothing that takes paint says so.

**Network (protocol 100).** `CActKind.Paint` (construct, mode | 4 for a link, part or link index, r, g, b) at the end of
`CActKind`; the server sends changes in `CBodyKind.Paint` (at the end of `CBodyKind`: construct, kind, index, colour; at
most 150 a message, flushed with the structure each tick), and `CBody` Full ends with the painted parts and links (so a
late joiner, a structure resend and a copy made for a save have it). Nothing allocates while painting. **Blueprints**:
a step and a link take `"paint": "#rrggbb"`.

**Self-test** "Paint tool": the wheel's segment and ring; only paintable boxes change (pane, wheel, lamp, panel); the
eyedropper; a link; whole-construct fill and Reset; the stroke acts on each swept part once; the refusals (tool, reach,
seat, bad colour, a defended construct in survival); the construct message and the change messages; the blueprint
round trip and bad colours; paint stays with its part when another is taken off; no allocation; and a real host with a
separate client and a late joiner agree.

### Choices made (the developer may change them)

- The paint tool took the hand drill's slot (north-west) instead of adding a ninth segment, which would have moved every
  other tool off its compass point. (The ground drill later needed a ninth segment all the same.)
- Reset acts on one part or link (hold and sweep to reset several); Whole construct paints, it does not reset.
- Painting is free in survival (a paint item could come later); only the whole-construct mode asks twice there.
- A box keeps its tone against the part's main colour instead of going flat.
- The recent colours are not saved between sessions.

## The remover tool (board item rt01)

The developer's request (2026-10-31). A red-banded eraser on the tool wheel's south-west segment (it took the scanner's place:
the wheel keeps eight segments and every other tool stays on its compass point). Code: `Source/Constructs/ConstructRemover.cs`
(the selection and the controller), `ConstructEditsRemover.cs` (the server), `ConstructWorldRemove.cs` (the batch removal),
`Source/App/Program.SandboxRemover.cs` (the keys), `Source/Game/GameSandboxRemover.cs` (the request); model
`Constructs/Tools/RemoverTool.json`. Protocol 104.

**Selecting.** **LMB** on a part or a routed link marks it (a red outline; a link is red through the parts); LMB on a marked
one unmarks it. **Hold LMB and sweep** to mark each part passed over once per stroke (a stroke that starts on a marked part,
or with **Shift**, unmarks as it goes; a press held still marks once and stays). **Double-click** a part to mark its whole
construct (a loose body is its own construct), **Shift + double-click** to unmark it. Only what the tool may delete is marked
(the aim highlight's rule, `SandboxAim.Remover`: yellow outline when it can, red with the reason when not). The HUD says
"N parts selected" (and links). The selection is this PC's (nothing is sent until the delete), kept when the tool is put
away, and dropped part by part if the construct changes under it (a mark remembers its part type and place and is checked every frame).

**Deleting.** **RMB** with something marked shows "Delete N parts? Right-click again to confirm" next to the crosshair (no
window: the game keeps running). A second RMB within four seconds sends the request. **LMB** (that click marks nothing),
**Esc**, putting the tool away, the four seconds running out, or the selection changing cancel the question and keep the
selection. **Shift + RMB** clears the selection; RMB with nothing marked says so.

**What it does.** The server (`ConstructEdits.Remover`; `CActKind.Remover`: u16 n, then per thing u16 body, u8 is-a-link,
u8 index) checks the tool, no seat, the reach, and per construct `RemoverRefusal`, all or nothing. The marked parts are
destroyed outright (nothing is handed to anyone, no scrap); routed links (and so wires) on a deleted part go too; a link
marked on its own goes alone; pieces cut off from the root split off as wrecks exactly as when one part is taken off
(`ConstructWorld.RemoveParts`: one flood, one split and one compaction for the whole batch, where `RemovePart` takes one);
a deleted container spills what it holds as loose pickups (the owner is nobody's); a whole construct (every part marked)
is removed. A merged receiver in the batch falls back to one `RemovePart` at a time (the merge settles after each).
Nobody may delete a construct someone is driving or sitting in.

**Modes.** *Creative* (infinite parts): free, instant, any construct. *Survival*: your own constructs; loose pieces and
wrecks unless their owner is defending them (decision 19, `Contested`); never someone else's working construct or the map's
("salvage it with the wrench or welder"). It gives nothing back. The map can turn it off for survival with
`"sandbox": {"removerInSurvival": false}` (the server refuses it: "the remover is switched off on this server").

**Performance.** 200 loose pieces and 60 parts of a 90-beam chain (260 targets) delete in one request in about 4 ms here
(the self-test's budget is 8 ms where CPU time is measured, Windows). No allocation per frame while the tool is out.

**Self-test** "Sandbox remover": the wheel segment, wire byte and model; through the controller `Program` drives, with
scripted keys and the game's own crosshair ray: click, click again, Shift+click, a stationary press, a sweep (and an
unmarking one), a double-click, Shift+RMB; the question deletes nothing and the second RMB deletes exactly the marked
parts, the cut-off end becoming a wreck; LMB, Esc, the timeout and the tool put away cancel it; a link alone and a part's
link; a chest's contents spilling; creative, survival (own, someone else's, defended, wreck, loose, the host's switch,
nothing handed back); the batch's time; no allocation; and a real host with a separate client agree.

### Choices made (the developer may change them)

- The remover took the scanner's slot (south-west) rather than adding a ninth segment, which would have moved every other tool off its compass point (as the paint tool did with the hand drill).
- Deleting is **not** part of creative's Ctrl+Z (sb01): the undo remembers placements, and putting back a construct with its links, wires and storage is not practical. Left out.
- Survival: allowed on your own constructs and on loose pieces and wrecks nobody defends; the map's constructs are refused. The host's setting is a map key (`removerInSurvival`), not a lobby setting.
- All or nothing: one refused construct in the request refuses the lot, so what was confirmed is what goes.
- A double-click needs the same part twice within 350 ms; a click on a different part is a new click.

## Spawning bots and zombies

Board items dev-sandbox-spawn-npcs-sn01 and dev-sandbox-spawn-bots-zombies-sb02, done as one feature (2026-10-30). Code:
`Source/Game/GameSandboxSpawns.cs` (requests, the queue, sides, the spawners' tick, zombies on constructs, bots round
constructs), `Source/UI/UiSandboxSpawn.cs` (the inventory's Spawn tab), `Source/App/Program.SandboxSpawn.cs` (placing:
the ghost circle), parts in `Constructs/Parts/Spawn`. Protocol 92.

**One category, "Spawn"**: the inventory has a **Spawn** tab (the direct spawns, clear all, the host's settings and a
button for each spawner part), and the two spawner parts sit in the Parts tab's **Spawn** group (section "Spawner
parts"), so a search or the tree finds them like any other part.

**Direct spawns** (creative worlds only: a map with `"infiniteParts": false` refuses them, "its enemies come from the
world"): Msg.CAct `Spawn` (u8 what, u8 difficulty, u8 gun kind, u8 its value, u8 zombie mix, u32 count, the aimed spot)
and `ClearSpawns`. The server checks the permission (the host, or everyone when the host allows it), the spot (`SandboxSpawnSpot`:
ground within 3 m below, not inside a wall, not on a construct; at most 150 m away) and the caps (a request over a cap is
cut down to it, with a note), then queues it. The queue spreads big asks over ticks: a bot a tick, 8 individual zombies
a tick, 10,000 crowd zombies a tick (500,000 take 50 ticks).

**Sides**: a spawned bot is a full bot (the bot AI, the nav graph) with team 10 (friendly) or 11 (enemy), replicated in the
mode state like any team. The Sandbox's `AreEnemies`: enemy bots against everyone else and never each other; friendly bots
never against people or each other; people still free-for-all among themselves. A friendly bot follows whoever spawned it
(a spawner's holds near the spawner), and with zombies within 22 m fights them as a Zombies co-op bot does; an enemy bot
hunts. Spawned bots never respawn: 3 s after dying they leave. Bots need player ids: at most 28 spawned bots (the host's
cap, 16 by default; one id is always kept free for a person joining).

**Zombies**: the Sandbox gives the Zombies AI a stand-in Zombies map (`SandboxZMap`: one area over the whole map, one
spawner out of the ground, no doors, windows or buyables, a crowd so the mass horde is on), ticked from the Sandbox tick
with none of the mode's rounds, points or downs: a zombie's hit is an ordinary hit and a death "killed by the enemy".
Zombies hunt people and friendly bots (`ZPrey`), not enemy bots. Up to 64 come as individuals rising at the spot (exactly
that many); a bigger group goes into the crowd round the spot, the first 4,000 as particles and the rest straight into
the mass tier (a count per cell), and the tiers wake the nearest as individuals as on a crowd map. `Game.StressMax` is now
500,000 (the Horde Stress lobby's custom size too). Constructs keep individuals out as they keep players out, and one held
off by a construct swipes the part in its way like a building door (40 damage, every windup + 0.6 s): doors and panels
give way. Crowd and mass zombies pass through constructs (they are far away). The zombie messages (ZEnts, ZEvent, ZCrowd)
go to Sandbox clients as in Zombies.

**Spawner parts** (`"logic": {"kind": "botspawner"}` / `"zombiespawner"`, role logic, the wrench): settings from the
configure tool (team, difficulty, gun class; or walkers / mixed types; rate a minute, group size, max alive, total (0: no
limit), radius, start on; a part takes up to 12 settings now) and named **data ports**: inputs **enabled** (port 0, the
part itself), **spawn now**, **rate**; outputs **alive** (port 0: how many it has out) and **spawned** (a one-tick pulse).
A data link names a port in its port byte (`0x80 | k`, k > 0; 255 is port 0), the linking tool's picker lists them, a
blueprint link says `"toPort": "spawn now"` / `"fromPort": "spawned"`, and the logic's wires carry the ports
(`CWire.FromPort` / `ToPort`). Enabled (its input on, or unwired and set to start on), a spawner spawns a group at once,
then one each 60 / rate seconds, while it has fewer than its max alive out and its total isn't reached (and the host's
caps allow); a rising "spawn now" spawns a group at once, on or off. What a spawner made carries its construct and the
part's stable tag, so it counts them back however the parts are renumbered; its zombies are never put back into the
crowd (they'd stop counting). It shows a light (blue, green) while on. A wire from another construct goes into port 0.

**Bots and constructs**: after a bot steers, a solid part box of a construct in the way (in the part's frame, a player's
radius round it, from a step up to head height: a floor it can step onto or a doorway between walls is not in the way)
turns it along the box's side towards its goal, and a bot held up against one for 1.5 s marks the path edge it was on
blocked for 8 s and plans again (A* goes round). Bots already step up onto builds with the players' mover. The nav graph
stays the map's (deterministic). Follow-ups for bots inside built buildings (from the building pieces work): the tops of
walkable parts as nav nodes, nav links through door openings (opened with Interact first), and a nav patch when a
construct is built, changed or broken.

### Self-test

**Sandbox spawns** (`Source/App/SelfTestSandboxSpawns.cs`): the parts' files and ports; 3 friendly and 3 enemy bots take
sides (friendly bots aim at enemy bots, never the host; an SMG class holds); mid-air, a survival world and the bot cap
refused; clear all; 7 zombies spawn exactly 7; 500,000 accepted and in the mass tier after 60 ticks, the tick time and
no allocation in 60 steady spawn ticks; a cap of 1,000 holds; a friendly bot reaches the host past a construct; a Zombie
Spawner on a switch (off: none; on: one at once, one a second, at most 3 alive, its alive output 3, its spawned pulse
toggles a toggle; refilled to its total of 5; spawn now off); a Bot Spawner's two enemy Easy shotgun bots; blueprint links
and the construct message round trip with their ports and settings; over the real transport a client refused until the
host allows everyone, then its zombies on both sides, a friendly bot and a clear all.

### Choices made (the developer may change them)

1. One "Spawn" tab plus the spawner parts in the Parts tab's Spawn group (not a Characters group).
2. Spawner parts work in survival worlds too (only the direct spawns are creative); a survival world gets them only as
   its rack or salvage hands them out.
3. Host caps: 16 bots (at most 28: player ids), 500,000 zombies; the host settings aren't saved between sessions.
4. Individuals up to 64 a group; spawner zombies are individuals only (max alive at most 64 a spawner).
5. Friendly bots follow the spawning player (3 m off); spawner friendlies hold near the spawner; enemy bots hunt.
6. Zombies leave enemy bots alone; enemy bots ignore zombies.
7. A construct swipe is a building door's (40 damage); the crowd and mass tiers ignore constructs.

## Building sounds

Every sound is synthesised at start-up (`Source/Audio/SynthBuild.cs`) and plays on the mixer's **Building** row (F8, key `build`), on 8 one-shot and 3 loop voices of its own (`Audio.Build.cs`), so building never takes a gun's voice. `BuildSounds.Play(kind, position, size)` is the one call: it plays on this peer only and nothing goes over the wire. Same-kind hits within 0.3 s and 7 m merge into a burst of at most 5 hits, so a paste of 200 parts is a short rattle. A clunk's size class comes from the part's mass (small under 40 kg, medium under 1.6 t, large under 63 t, else capital) and its flavour from `material`. Ticks for the ghost, node clicks and refusals play where the input knows them. Everything else comes from `Game.SandboxBuildSounds` (once a frame, the same on host and client), which watches what every peer already has: part and link counts per body, bodies appearing and going (`ConstructWorld.GoneRing`), and the hands in the state tail (edits in progress give the welder's arc and the wrench's ratchet, a weld that ends near its finish a tick, the physics gun's hold its cues). A change only sounds with a player near it holding a tool other than the physics gun, so a part shot off, a joiner's world and machines stay quiet. New bulk actions need no call. `size` is a mass in kg.

## Snappy building (decision 24, board item sb01)

Building is quick: hold a part and its connection points show, the aim is helped by a screen magnet, and a row of parts
is one sweep of the mouse. The server still decides everything; the client's ghost is only a preview.

- **Connection types** (`ConnType`, `Source/Constructs/SnapBuild.cs`): Mount (a structural face), Barrel, Muzzle, Optic,
  Feed, ShaftIn, ShaftOut, Fuel, Power, Data, Hub, Leaf. Part files name none of them: `SnapBuild` reads a type from the
  part's role, the socket's face and a gun part's category, so no part file changed.
- **Markers**: while a part is held, its own connection points are drawn on the ghost and every free socket on the aimed
  construct it can use is drawn too (not only near the aim; they fade out with distance), colour-coded by type
  (`SnapRender`, `SnapBuild.Colour`). Sockets the part cannot use are hidden.
- **Single-purpose parts** (barrel, muzzle, optic, feed part, wheel, door: `SnapBuild.SinglePurpose`) show and snap only to
  their functional sockets. The screen magnet (`SnapBuild.Magnet`) takes the cached socket nearest the crosshair within
  80 px at 1080 lines (scaled with the window height), nearest by screen distance and then depth. A barrel therefore
  cannot go on a frame face.
- **Multi-connection parts** (gearbox, engine, seat) mount by their structural face first. Where one of the placed ghost's
  functional points lines up with a free matching socket (`SnapBuild.Lineups`), the ghost marks the connection that will
  be made. **K** (Switch connection point, binds version 15) steps to the next of the part's own sockets that fits;
  F is Interact and the middle button the tool wheel.
- **Item parts only**: welder and physics-only parts cannot be ghost-attached from the hands (cargo volumes, decision 21),
  so snapping from the hands applies to item parts.
- **Feel**: the ghost eases to a new socket, a soft click and a flash on a new snap, a heavier thunk when the part lands.
- **Creative only** (infinite parts, and the player has asked for it with the `snap_build` setting, on by default):
  quick building fits at once and keeps the part in the hands; **hold Fire and sweep** to place the same part on each newly
  hovered socket (once per socket per press); a quick **middle-button tap** on a part picks its kind into the hands (a
  held middle button is still the tool wheel); **Ctrl+Z** undoes the last placements (8 kept per player, server-checked:
  the construct is still theirs, in reach, and the part where it was put); **L** (hold, flick, let go) is a wheel of the
  last 8 placed kinds.
- **Cost**: matching sockets are cached per held type and per construct (`SnapCache`), rebuilt only when the construct's
  signature (parts, turns, links) or the held type changes. A steady frame hashes the construct and allocates nothing.
- **Network** (protocol 94): `CAct.Snap` at the end of `CActKind` (undo, quick building on or off, pick a part off a
  construct, take a kind into the hands). No new messages.

**Self-test**: "Sandbox snap build" (`Source/App/SelfTestSandboxSnap.cs`): a barrel sees only a receiver's front and the
magnet takes it from a near aim but not far off; a muzzle sees nothing on a bare receiver; a gearbox mounts by a face and
a switch lines its shaft input with an engine's output; every part with sockets reports its points; the switch key; six
lamps with one held part; pick; undo (and the refusals); the cache rebuilds once per change and allocates nothing.

Choices made (the developer may change them): K and L are the new keys (F and the middle button were taken); the magnet
is 80 px; 8 placements are kept for undo; quick building and paint are creative-only; survival keeps one press per part.

## Centre sockets (board item cs02)

At load, a frame, panel or foundation part gets a structural socket named "centre +y" (and so on) at the centre of each large
face (both sides at least `ConstructParts.CentreFaceMin`, 0.75 m), unless one of its own sockets faces out of that face within
`CentreNear` (10 cm) of the centre, or the centre isn't solid. They are appended after the file's own sockets, so no socket
index in a link, blueprint or save moves. `"centreSockets": false` opts a file out. Parts on the 0.25 m cell lattice
(Structure, Building) get one only where it lies on the lattice; damage stages and the parts that turn into them are left
alone (they must keep identical sockets). Generated weapon parts and joints are not touched. "Centre sockets" checks all of it.

## Performance and caps

A construct holds at most 8,000 parts, 24,000 links, 255 joints and freeform welds (64 welds), 256 wheels and track rays,
256 spring feet, 64 guns, 254 routed links and 3,000 link nodes, 1,024 wires ([Size classes](#size-classes-the-capital-parts)
says what limits each). 4 inputs a part, wires up to 8 m (12 m between constructs, 64 of those), 32 couplings, 16,384
contacts a tick (each rigid group's points against one thing thinned to nine a normal). Fixed arrays throughout: the self-test drives a buggy round the yard for 120 ticks and
measures **no allocation** (with the turret turning, the lift and the motorised gate moving and their logic running);
the yard's constructs take about 0.1-0.4 ms a tick in the container. The logic step is a few spans on the stack. Sleeping bodies cost
nothing. Other modes never touch any of it (every hook returns at once outside Sandbox; the seeded mode digests are
unchanged).

## Self-tests

The **Flexible joints** section (`Source/App/SelfTestFlexJoint.cs`): the parts and the joint cap; a beam on a rubber joint droops where load / stiffness says, a wrecking ball takes it to its stop past its rating and off again it comes back with little overshoot; softer settings droop further, a signal locks it; kicked every way it stays inside the circle and a ball joint twists; a wrecking ball on four parts at both ends of their stiffness, kicked at random, stays finite, in one piece and settles, and a free 30 kg block carrying one stays sane; a block on the leaf by the wrench, socket welds on and off the grid and a freeform weld hangs and swings alike; a motor hinge beyond it works, a wheel beyond it is a plain part; a player stands on the arm with both movers and bends it; a chute over a moving motor hinge, piston, turntable and ball joint carries bulk, one over a hinge carries parts; a piston past a section's reach stops it with the reason and it carries on coming back; a turntable stops one at 135 degrees, tears it past that with a hint, and the wrench mends it; a cargo hose moves a load a second and pops out at its length unhurt; nothing allocates. **Flexible joints network**: a real host, a client with lag and loss and a late joiner see the same joints, axes, angles and strain, a torn chute, and a cargo hose the client plugged in carrying loads.

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
the dozen inside 2 ms a tick. Most of it on a flat test floor with box walls (`JointTestWorld`).

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

The **Construct salvage** section (`Source/App/SelfTestConstructSalvage.cs`, milestone 3): the recycling yields and the
map's `infiniteParts`, `debrisSeconds` and `debrisNear` (bad values refused); in an offline Sandbox game on the yard a
wheel that fell off the buggy as a wreck is salvaged (the welder refused, the wrench timed) into the inventory with the
hand left empty, a lone wrecked suspension is not salvaged on site (it is hauled to a Recycler), someone else's working gate is refused until a part of it is
wrecked and then salvaged; with infinite parts off the parts menu refuses a part the inventory lacks, hands one out of
it, a fit uses it and the next comes into the hand, the player's own gate's door goes back into the inventory, a held
counted part goes back on respawn, and the HUD shows the counts; with them on the player's own parts come off into the
hand; a wreck left alone goes after the map's time, one being salvaged or with a player near stays; the Sandbox tick,
the debris check and the inventory allocate nothing; and a real host with two clients: the salvaging client sees its
count (the other doesn't), a client's gate is owned by that client on every copy, and an expired wreck goes on the
clients too.

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


The **Sandbox tool wheel** section (`Source/App/SelfTestSandboxToolWheel.cs`): the direction-to-segment maths (every
segment's middle and both edges for 8, 4, 3, 6 and 1 segments, the wrap-around at the top, the dead zone, a pointer that
isn't a number), the pointer held at its reach so one flick turns it; the key (an unshared middle mouse button; binds
version 9 files get it, one that already used the button keeps it); in an offline Sandbox game a scripted
hold-flick-release takes out the welder (east), wrench (north), linking tool (west) and hands (south), the current tool is
marked when it opens, letting go in the dead zone cancels, the scanner says "coming later" and changes nothing, the
north-east takes out the physics gun, a menu and
death close it without choosing (and the still-held key doesn't reopen it), and it doesn't open in a seat; 240 headless
frames with the pointer going round (the linking tool's outer ring among them) draw without an ImGui error and allocate
nothing; and a real client's pick reaches the server.

The **Sandbox tool wheel outer ring** section (`Source/App/SelfTestSandboxWheelRing.cs`): the outer ring's arcs (every
option's middle and edges for 13, 2, 3, 9, 1 and 10 options, centred on the tool, the far side of a partial arc picks
nothing, the arc or full circle choice); the linking tool's options are the modes only (route, delete, move node, re-route) and the highlight toggle, with no link type and no arc; in an offline Sandbox game pointing at
the linking tool shows its ring with all nine, animating in; flicking out onto each option and letting go takes the linking tool out with it; letting go on the
tool keeps the last option (marked on the ring), and in the middle cancels; a welder given two test modes picks from its
arc, and outside the arc the pointer turns the inner ring; and every tool's and option's name, the title and an over-long
name fit the centre circle at UI scales 0.75 to 2 (shrunk, or cut short only for the over-long one). The third tier (a test set with a five item arc on the welder slot, since the pipe lost its styles): five items span 125 degrees centred on
the option; resting on it opens the arc with nothing picked; entry and keep distances (no flicker); an item calls `ChooseSub`,
letting go on the option calls `Choose`; moving to the next option or inward closes the arc; the "Sandbox tool wheel" section
draws it with a sub-option hovered and checks it allocates nothing.

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


## Decisions made for milestone 1 (open questions it touches; the developer may change them)

1. **Joints**: only the passive hinge ships, as a reduced-coordinate angle on its body; the joint record is ready for a
   motor (turntable) and a slide (piston), which milestone 2 adds with the linking tool and logic parts.
2. **What players start with**: a gun loadout as usual, both tools always available (keys 3 and 4), and an endless
   parts rack on the map. No resources, crafting or unlocking yet.
3. **Destroyed parts** break off as wrecked debris (a wrecked part to haul to a Recycler); engines, tanks and seats are disabled
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
bolted to a pivot's leaf, and the wheel on it, turn with it, and its axle sets the tyre's direction. Caps: 16 joints a
construct (a flexible joint takes 2 or 3), 128 in all.

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
- A player standing on a part **rides in its segment's frame** (`ConstructRide.cs`, board item rf01; protocol 102): the
  feet are kept in that frame (`MoveState.RideLocal`) and put back in the world by its pose each tick, exactly (no drift
  on turns, no slide on a pitching deck), and their velocity is their own, relative to the surface, so walking towards
  a truck's front goes towards its front however it turns. The local view turns with the frame
  (`ConstructRide.TurnView`). A jump stays in the frame inside the construct's bounds (or 0.3 s after leaving its
  surface) and lands where it took off. Leaving (walking off, jumping clear, landing on the map or an anchored floor, a
  mantle, a blast) adds the surface's velocity, linear and turning, so stepping off keeps its momentum, with weak air
  steering for 0.75 s (`MoveState.RideFree`). A crash (the deck's acceleration over 2.5 g across, or dropping away at
  over 1.6 g; server only) throws them off at the deck's speed before it. Everything runs in `BlockPlayer`, the step the
  server, the client's prediction and its replays share; the snapshots carry the frame-local feet (the own block
  exactly), a client replays in the frame and draws its riders in the construct as it is drawn, so lag shows as the
  vehicle a little behind, never as the rider sliding. CPose carries each body's turn rate. They count towards a
  joint's load. Anchored builds' own floors are not ridden (they never move).
- **Grip** (rf01 phase 2): a part with the `grip` setting on (a configure toggle on the Deck Plate and the Panel; the
  **Grip Deck** has it on, with a tread) holds riders on its face up to nearly vertical (about 84 degrees) and through
  up to 5 g; on it gravity doesn't pull them down the face and the walk goes along it (forward into a steep face climbs
  it). Stepping off its edge lets them go. Simplified for now: the body and the camera stay upright (world down). (This is bots' ride
  now; people: [Contraption space](#contraption-space).)
- **Boarding assists** (board item ba01, all in the shared step): coming down within 0.3 m of a moving construct's top
  lands on it at its speed (no slide off its back); walking into a construct's part steps up onto it from 0.6 m;
  jumping into a ledge up to 1.4 m high vaults onto it (a short climb in its frame, `MoveState.RideClimb`); Use on a
  vehicle's side (or on a **Grab Handle**, **Boarding Ladder** or **Step Plate**: `"boarding": true` parts, taken
  first) latches on and pulls the player up onto the nearest top within 1.5 m across and 2.6 m up (`ConstructRide.GrabOn`,
  server); a moving construct's seat is reached up to 1.6 m further (0.2 s of its speed, to 8 m/s). Friendly bots whose
  leader sits in or rides a construct take its free passenger seat (or walk to it). The "Ride frame" and "Boarding
  assist" self-tests cover each case.
- **Run-over damage** (`ConstructWorld.BlockPlayer`, `Game.SandboxRunOver`): only the **construct's own motion** counts:
  the velocity of its part at the contact (the body's motion, linear and turning, plus every joint's up its chain: a
  swinging arm, a turntable) along the contact normal towards the player, less the player's own speed away from it
  (ba01: jogging alongside a vehicle is safe), in the frame the player rides (a truck bed's walls don't hit its
  riders). Running into it never adds, so
  sprinting into a parked car only stops you, and landing on a parked car is a landing (normal fall damage only). At up
  to `Game.RunOverSpeed` (4 m/s, tunable) the part only pushes the player along at its speed; faster, it does
  `(speed - 4) x 9 x sqrt(mass / 500)` damage (the mass factor 0.4 to 2.5), at most once in 0.6 s, and knocks them out
  along the contact and up, harder the faster and heavier it is (`Game.RunOverKnock`). The same rule holds for moving
  joints and wrecks. The driver gets the kill (shown as a melee kill), or nobody for a driverless construct. The
  "Construct run-over" self-test covers each case.

### Contraption space

A player on a construct lives in its frame (`Source/Constructs/ConstructRideMove.cs`, the look and drawing in
`Source/Game/GameSandboxRide.cs`; protocol 130). This replaces the plain ride above for people; bots keep the plain ride.

- **The frame.** A branch of `World.Simulate`, like noclip: with `MoveState.RideUp` set, the tick runs `ConstructRide.Step`:
  the capsule mover's walk, step-up, ground snap, jump and stances (`SweptMover.FrameStep`, the same code the world mover uses)
  in the coordinates of the segment under the feet, against shapes gathered there. `RideLocal` (feet) and `RideVel` (their
  velocity) are the whole of their movement; `Position` and `Velocity` are copies for whatever reads the world. The construct's
  own boxes come from its joints alone (`SegPose`), never its world pose, so nothing about its speed, turn, tilt or roll
  reaches the mover: the same inputs give the same local state at 0 and 200 m/s, tilted or inverted, on the server, in the
  client's prediction and in its replays. A segment is the moving joint's (`MoveSeg`): a freeform weld is the same frame.
  With `RideUp` unset, nothing here runs: every other mode and every non-rider moves as before.
- **Gravity and up.** One direction for the whole construct, taken when boarding (`ConstructRide.UpFor`, in the rest frame): the
  drive seat's up; with no seat, the direction most of its faces lie across, the way nearest the world's up. If the surface
  under the feet doesn't lie across that (a car on its side), that surface's normal is the rider's up. A joint's segment carries it
  round with it: a raised drawbridge is level to who stands on it, and crossing back rolls the view. Gravity is the world's
  strength along -up in the frame, also in space. There is no Up part or setting yet.
- **Grip faces** stay, and do more: a grip face's own normal becomes the rider's up, at any angle, so they walk up a grip wall
  and across a grip ceiling with the body and view rolled onto it. Ordinary faces no longer need Grip to be held on: a construct
  tilted 80 degrees is level to its rider. Stepping off a grip face onto an ordinary floor goes back to the construct's up.
- **Boarding.** Standing on a top as before, or walking or jumping into a face that is the construct's own up but too steep to stand
  on in the world (a panel raised at 60 degrees; `SteepBoard`; a truck's side isn't its up, an overhang never takes anyone).
  Stepping onto one from the ground works to about 65 degrees; steeper, jump at it. A frozen build's floor is only ridden if its
  up leans over 15 degrees from the world's; a level base stays the world's ground.
- **Leaving.** Walking off an edge or jumping clear: off every surface they stay in the frame (falling along its up) inside the
  construct's bounds (0.5 m round them, 2.5 m along up) or for 0.3 s; past that they leave with their velocity plus the surface's
  (world gravity from there, weak air steering for 0.75 s). So jumping on a raft at 200 m/s lands where it took off. Also: a
  blast's push; touching the world's geometry (below); a seat; noclip (which ignores the frame); a crash.
- **A crash** is a jolt over 15 g (30 on a grip face) in the deck's acceleration less what its own turn explains (`CrashAccel`), so
  a hard turn at speed never throws anyone, and a wall does. Thrown riders leave at the speed before the jolt, not rejoining for 0.5 s.
- **Other things in the world.** Other constructs are in the frame's shapes, each posed relative to the deck: they block, carry
  (standing on one joins its frame) and, closing at over 4 m/s, hit as a run-over does; over 8 m/s they scrape the rider off. The
  map, terrain and movers are not in the frame: each tick the body is tested against them as it stands in the world (the upper
  body when standing, the whole capsule in the air); touching them ends the ride at the deck's speed. So a low bridge at speed
  takes you off the deck, and walking off a ramp onto the ground just lands. A ride ended that way can't start again for 0.5 s.
- **Doors, plates, lifts.** Walking into a door leaf swings it; pressure plates, a joint's load and spring feet count riders;
  a lift's deck is a segment, so it carries its riders exactly, and stepping off changes frame.
- **The look.** The client keeps the look in the frame (`RideLookSync`): any change to `LookYaw`/`LookPitch` (mouse, recoil) is a
  change in the frame, and they are worked out again from it as the construct is drawn, so every aim ray and tool that reads
  them is right whichever way it's rolled. Input carries the look in the frame (a trailer on `Msg.Input`); the server works out
  the world look from it and its own pose (`RideLookIn`), so reach and aim agree in the frame. The camera's up is the frame's,
  exactly; moving onto, off or between frames rolls over about a quarter of a second (`RideCamUp`). The head bob and foot IK are
  off while riding.
- **Drawing.** Every frame, the host and each client draw a rider from their frame-local feet and the construct's drawn pose
  (`SandboxFrameRender`): nothing lags between them. Remote riders use the snapshots' feet in the frame and their up.
- **Tools.** Actions name parts and sockets, and points in a body's frame, so they mean the same at any speed; the server judges
  reach from the rider's eye, which is exact in the frame. Riders on the server are put back in the frame after the
  constructs' step (`SandboxRidersFollow`) whether or not an input came. The host's input and overlays see the ridden construct at its
  drawn pose (`RideDrawnPose`; a host draws between ticks, and 3 m a tick matters to an aim ray). The physics gun holds in the
  frame: its target and turn are relative to the deck (`LookQ`, `GrabFrameVel`, `GrabFrameSpin`), and `HeldSweep` tests the
  piece's move in the deck's frame. Pieces cut free and wrecks already take the deck's velocity at the cut, a dropped part now too.
- **Shots.** At deck speed over 2 m/s a rider's shot uses the server's eye and look (the client's world ray is a round trip
  behind). A target on the shooter's own deck is rewound in its frame. Hit boxes stand along the rider's up.
- **Space.** A rider stays in the frame through a warp, a jump and being parked: they follow the pose. A ship on rails throws nobody.
- **Saved worlds.** A rider's place is kept in the frame (the `Riders` section, which older readers skip), and they come back
  aboard where they stood, relative to the construct.
- **Cost.** Eight riders on a 7,500-part, 300 m deck: 0.16 ms a tick in the container, nothing allocated.

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

## Joint sides (board item js01)

A joint part (hinge, motor hinge, turntable, piston, steering pivot, lid hinge, every actuator size) has two sides. The **base** is
what attaches to the construct and stays with it (every socket but the leaf); the **moving side** is the leaf socket and everything
that hangs on it (it carries the joint's segment). The part files say which is which:

- **Model.** Boxes carry `"side": "base"` or `"leaf"`; the loader refuses a joint part that tags neither (`--check-construct-parts` prints
  the reason). Base: a dark body, a mounting flange and four bolts. Moving side: a safety-orange collar over the leaf half with a stepped
  arrow on it (one head and a tail tick for a turning joint, a head at each end for a piston). The orange boxes are details and not
  paintable, so paint never hides the sides. `tools/constructs/make_joint_sides.py` writes them (idempotent; `--check`): fix the script,
  not the files.
- **Markers and ghost.** With a part held, a joint's moving-side socket is an orange disc with a bar, its base sockets a steel-blue plate
  with four bolts (`SnapRender`); the held joint's ghost draws its moving-side boxes orange (dark red when it does not fit), and the HUD
  says which side mounts where ("the Frame Block will ride on the Hinge's moving side").
- **Look-at.** Aiming at an assembled joint says "Hinge - Base: fixed to construct 7 (12 parts) | Moving side: 3 parts" and, for 2.5 s after
  the crosshair lands, outlines the two groups in two colours (steel blue: the base side, orange: the parts on its segment and every
  segment hanging from it; at most 48 parts a side). With the configure tool out the moving side also swings (or slides) through the
  joint's range as a pale ghost. `JointSides` (Source/Constructs/JointSides.cs) holds the side logic; nothing in it allocates.

## Freeform welds (board item fw01, decision 25)

The welder has three modes on the tool wheel's outer ring round it (`SandboxWeldModes`): **Socket weld**, the default
(held parts snap onto sockets, as before), **Freeform weld**: position a piece by hand with the physics gun at any
angle and offset, then weld it to the vehicle exactly where it is, and [Point weld](#point-weld-board-item-pw01): click
a point on the piece, click where it goes, spin it, weld.

### Using it

- **LMB** casts the crosshair's ray through the first part it hits and on to the next part behind it on a **different
  body** (`Freeform.Behind`); those two are welded at their current relative position and turn. No socket, no grid.
- **Two clicks** when the ray finds nothing behind (parts side by side): the first LMB picks a part (outlined amber), the
  second LMB on a part of another body welds them; LMB on nothing starts again.
- **Preview** while aiming: both parts outlined and the weld bead drawn between their closest points, green when the weld
  is allowed and red when not, the reason on the look line. The held part isn't fitted in freeform mode.
- **Cutting** (either mode): the welder's aim (RMB) on a freeform seam (within 0.6 m of its bead, on either of its two
  parts) cuts it: the piece falls away as a body of its own where it is, moving as it moved. Destroying the anchor part
  (or the welded-on part) detaches it like any part.

### Rules (`Freeform.Check`, `ConstructEdits.FreeformRefusal`)

Two different bodies (a loose piece, a wreck, another construct); the closest boxes of the two parts within **0.25 m**
(touching isn't needed: the bead bridges it); neither part wrecked; at most **8** freeform welds a construct (they share
`Construct.MaxJoints` with the joints, and the world's joint cap); the welded-on piece without raycast wheels, not riding
in a cargo volume and not carrying cargo; nobody driving or sitting in it (as joining). Then the usual welder rules: the
welder out, out of a seat, within reach, your own constructs or ones anyone may salvage (decision 19), the contested rule
(`ConstructEdits.Contested`) and, in survival, the weld takes **2.5 s** (`EditKind.Weld`, checked again when done; creative
welds at once). The physics-only rule (decision 21) is unchanged. Which body keeps its frame: one held to the ground (a
foundation or pinned, not just frozen), else the one that isn't a loose part, else the one with more parts, else the
second picked; the other becomes the freeform segment. The welder's owner takes the result, as joining does.

### How it works (`Source/Constructs/ConstructFreeform.cs`)

A construct part still turns only in Rot24 steps on its segment's 1 cm grid. The welded-on piece (its whole body) joins the
construct as a new rigid **segment** whose rest frame sits at a fixed, arbitrary pose on its parent segment: a joint of type
`JointType.Fixed` (wire byte 5) with `CJoint.FixRot` / `FixPos` (`Construct.SegPose`), its anchor part in `Hinge` and the
welded-on part in `Child`. Everything inside the segment stays on its own Rot24 grid, so socket parts attach to a freeform
panel as usual (they go on its segment; `ConstructBuild.Blocked` compares parts in the same frame, `Construct.FrameOf`).
Nothing solves it: the physics skips freeform welds (no slot, no chain entry, no motor), their parts' corners are **hull
points** of the body (`Construct.Rigid`), and the mass, centre of mass and inertia take them turned into the body frame
(`Construct.RestPose`, `RestBounds`; each box's own inertia turned with it). It costs what a jointed segment costs minus
the joint, and the moving-joint passes only run for constructs with a moving joint (`Construct.MovingJoints`). Collision,
damage rays and drawing already work per segment. The weld is an edge in the connectivity flood, so losing the anchor or
the welded-on part splits the piece off where it is (`ConstructWorld.Compact` moves the body's frame to a freeform
segment when what stays hangs beyond a weld that went, so nothing jumps). A cut marks the weld and runs the same split.

### Network, blueprints and saves (protocol 97)

`CBody` carries the freeform welds at its end (each one's joint, welded-on part, position and quaternion as exact
floats); `CActKind.FreeformWeld` (u16 body, u8 part, u16 second body, u8 its part) and `CActKind.FreeformCut` (u16 body,
u8 part, u8 weld) at the end of `CActKind`; `EditKind.Weld` in the hands tail. A blueprint step may be a weld instead of a
socket step: `{ "part": "Panel", "weld": { "on": 3, "pos": [x, y, z], "rot": [x, y, z, w] } }` puts the part on a new
freeform segment, its own frame at that pose in step 3's segment frame (`ConstructBuild.WeldJson` writes one from a weld,
`Freeform.StepPose`). Constructs aren't saved with the world yet; the construct message and the blueprint step are what
a save will use.

### Self-test

"Freeform weld" (`SelfTestFreeformWeld.cs`): a panel held at 30 degrees over the buggy's plate is welded by one
ray-through click without moving and stays exactly there (to a micrometre) while the buggy drives 30 m and turns, with no
allocation per tick; the two-click fallback welds two panels side by side; a gap of 0.6 m is refused with the reason;
a beam socket-attaches to the freeform panel on its segment; the welder cuts it free where it was, beam and all;
destroying the anchor detaches the panel where it was; survival's weld waits its 2.5 s; a fresh copy from the construct
message and a blueprint with the weld step put the panel at the same pose; a real host with a separate client welds and
cuts over the transport and the client's copy agrees on the pose; the welder's default mode is socket weld.

### Choices made (the developer may change them)

- The gap limit is 0.25 m and a construct takes 8 freeform welds; weld strength is a normal weld's (the anchor and the
  welded-on part take the damage).
- Cutting is the welder's aim on the seam (RMB within 0.6 m of the bead), and it is instant (survival included);
  welding is LMB.
- In freeform mode the held part isn't fitted (switch back to socket weld on the wheel to fit it).
- A piece with raycast wheels can't be freeform-welded (wheels roll in the body's own frame); a part on a freeform piece
  can't be socket-joined to another construct with F (weld it freeform instead).
- **Wheels** (the "Wheel welds" fix): a freeform weld of a wheel to a suspension whose free axle sockets are within
  0.35 m and face each other (`Freeform.AxleSnap`) is done as the socket weld of the axle onto the axle at the nearest
  grid turn, so the wheel rolls and springs as a wrench-fitted one does (the look line: "puts the Wheel on the
  Suspension's axle (it rolls)"). A wheel welded freeform to anything else is a **fixed wheel**: welded on, it doesn't
  roll, and the look line says so.
- A routed link copied in with the welded piece keeps its nodes in their host parts' frames.

## Next: milestone 3 and later

- **Salvage** (done, above). Still to come with it: materials, repair, saving inventories.
- **Physical storage** (done, above). Still to come: carrying limits (the developer's question), more storage parts, saving with the world.
- **Construct weapons** (done, above). Still to come with them: artillery (craters are in: docs/sandbox/TERRAIN-DIGGING.md), guided or
  flying rockets, a zoomed gunner view through an optic, enemies manning guns.
- **Resources:** [gathering](#gathering-decision-14) and [refining](#refining-decision-14) (done, above); assembling comes next. **Enemies** that drive and defend constructs, and **factions**.
- Links between constructs and power (done, above).
- Saving constructs with the world, then the large world, streaming and points of interest.
- A bigger map built over several iterations, as the developer asked (bumpers, fenders, gauges, batteries and gears came in milestone 3).

## Milestone 1's list, as milestone 2 left it

- Done: the linking tool and logic parts (seat -> input reader -> piston), motorised hinges, turntables, pistons, steering
  as a joint, transmissions and differentials as parts you can shoot, passengers, standing on a moving deck, run-over
  damage, lights (the Lamp).
- Still to come (above): construct weapons, saving, the large world, more parts, a bigger map (salvage and debris
  expiry came in milestone 3).

## Socket weld (board item sw01, decision 26)

The welder's default mode, **Socket weld** (the outer ring's other mode is [Freeform weld](#freeform-welds-board-item-fw01-decision-25)),
is a three-step flow. Sockets are an alignment and snapping aid here, not a strict connection system.

1. **Pick the source socket.** With the welder out and nothing in your hands, the aimed body and the bodies nearest
   the crosshair (at most four, within 7 m, at most 64 markers, the nearest the crosshair) show their sockets as a dot
   and a short arrow along the socket's facing: blue free, grey in use, orange the picked one, green a target, white
   the one the crosshair snaps to (within about 4 degrees). LMB on a socket of the piece to move (a loose body or a
   separate construct) picks it; the piece stays where it is.
2. **Pick the target.** LMB on a socket of the construct to weld to (the magnet snap), or on any flat face of one of
   its parts: the 1 cm grid point under the crosshair on that face is a virtual socket. A ghost of the piece shows its
   socket on the target point, facing opposite the target's normal, turned to smart rotation's best guess (a one-part
   piece onto a socket; others start at 0), green or red with the reason on the look line (an overlap, the ground,
   someone standing there, someone else's construct, contested, a full construct, wheels off the grid...).
3. **Spin it about the socket axis.** Mouse X (the view holds still) or the wheel turns the ghost; it snaps to 15
   degree steps with a click on each 90 degree step and a ring of ticks round the socket (bigger on 90), Shift for free
   angles, Ctrl for 5 degree steps. R takes the next facing smart rotation ranked (else half a turn: upside down). LMB
   confirms: the weld takes the piece's fit time in survival and is instant in creative; the body snaps to the ghost.
   RMB steps back one step (3 to 2 to 1 to nothing; at step 1 RMB is the welder's usual cut free), Esc cancels.

**On the grid or freeform.** A multiple of 90 degrees onto a real free socket of the same kind on the construct's main
body keeps the piece on the construct's Rot24 grid: the two merge as a join does, linked socket to socket, the parts
on the centimetre grid. Any other angle, a face point, a socket of another kind, or a target on a joint's or a freeform
piece's segment joins the piece as a fixed-pose rigid segment, exactly as a freeform weld does
(`JointType.Fixed`, `ConstructWorld.FreeformWeld`; at most 8 a construct, no wheels on such a piece). The welder cuts
either free again (a grid part with RMB held, a freeform seam with RMB on its bead).

**Wheels.** A wheel's axle onto a suspension's axle socket always goes on the grid, at any spin (a wheel turned about
its own axle is the same wheel: it snaps to the nearest grid turn), and a rolling piece (a wheel or a suspension, no
joints of its own) onto a socket on a moving joint's segment (a steering pivot's) merges onto that segment
(`JoinPlan.Seg`) instead of becoming a fixed-pose segment: the wheel rolls, steers and springs exactly as one the
wrench fitted. Before this fix a wheel welded at a non-90 spin, or onto a steering pivot's suspension, was held on a
fixed segment and acted rigid. A welded wheel off a suspension's axle (a face point, an off-grid angle elsewhere) is a
fixed wheel; the look line says "a wheel on it is fixed and won't roll". **Self-test** "Wheel welds"
(`SelfTestWheelWeld.cs`): the buggy's wheels taken off and welded back (socket weld at 0, 15, 90 and 200 degrees, two
of them on the steering pivots; freeform welds 4 cm and 4 degrees off each axle) give the wrench's four raycast wheels:
they spin when driven, the springs carry the car and compress landing a 0.35 m drop, it drives and turns as the
blueprint's buggy does, a client's copy from the construct message has the same wheels; a wheel welded to a beam is
fixed; driving allocates nothing.

**Joints (board item wh01).** A weld onto a joint's leaf socket (a hinge's or motor hinge's leaf, a piston's or
turntable's top, a steering pivot's leaf, every actuator size) always hangs the piece on that joint, as the wrench's part
does: the welder and the wrench share the leaf code (`ConstructBuild.OnLeaf`, `LeafRefusal`, `HangOnLeaf`: the joint is
made at rest, or an empty one it had is used again). On the grid it merges onto the joint's segment, linked to the leaf
(the wrench's pose exactly); off the grid (any spin) it is a fixed-pose segment whose parent is the joint's segment, so it
moves with the joint. A freeform weld whose anchor is a joint part and whose nearest point on it is on the leaf's end
(`Freeform.OnLeafSide`) hangs on the joint the same way; one onto the base side stays rigid with the base. A piece on
the leaf that would also weld to the joint's fixed side (a free socket of it meeting one there, face to face) is refused:
"that would lock the joint" (`SocketWeld.LockRefusal`); only resting against it, as a lid on its chest, is fine. The look
line says "on the X's moving side". Two bugs were behind the report: the socket weld merged a piece onto the joint
part's own (base) segment, linked to the leaf but with no joint made, so pistons and turntables built with the welder
carried nothing; and `Construct.Rebuild` gave every joint whose anchor part is a joint part that part's joint type, so
a fixed-pose segment anchored on a hinge (an off-grid weld, a freeform weld on it) turned into a second joint about the
body's origin and lost its pose: the piece showed up mirrored through the hinge. Freeform welds now keep their type.
**Self-test** "Joint welds" (`SelfTestJointWelds.cs`): every joint part on a powered, anchored rig with a lever wired
in (the lid hinge on the Chest); a panel (a door on the passive hinge, the chest's lid on the lid hinge) socket-welded on
the grid and 30 degrees off it and freeform-welded onto the leaf sits outward at the ghost's pose (the wrench's within
1 cm and 1 degree), hangs on the joint, a client's copy agrees, and the lever drives it out and back as the wrench-built
one moves; base-side welds stay rigid; a weld onto both sides is refused; driving allocates nothing.

**Smart rotation learns** from step 3: confirming a grid angle other than its guess records the correction for that
part, socket kind and side, as fitting a held part does.

**Server and network (protocol 99).** The client sends `CActKind.SocketWeld`: the piece and its part and socket, the
construct, its part and socket (255: a face point, then the point in that part's segment rest frame and its facing),
and the angle in hundredths of a degree. The server checks the final pose with the same rules (`SocketWeld.Check`:
the sockets, overlap with the construct, the map and players, the merge and freeform limits) plus reach, ownership and
decision 19's contested rule, and in survival again when the weld time is up (`EditKind.SocketWeld`, shown as
"Welding..."). Code: `Source/Constructs/SocketWeld.cs` (rules, pose, markers), `SocketWeldFlow.cs` (the steps),
`ConstructEditsSocketWeld.cs` (the server), `Source/App/Program.SandboxSocketWeld.cs` (the input),
`Source/Render/SocketWeldRender.cs` (markers, ghost, ticks).

**Self-test** "Socket weld 3-step" (`SelfTestSocketWeld.cs`): decision 27's tools by function over every part file;
an aimed loose beam lists its markers (no allocation); its end socket on a frame block's side: the ghost aligned and
facing out; a 30 degree spin welds it as a fixed-pose segment exactly at the ghost, 90 degrees stays on the grid
(merged and linked) and smart rotation learns the correction; R and RMB step by step; a red target (an overlap) is
refused; a face point in survival takes the fit time; the construct message and a blueprint keep the pose; a real host
with a separate client welds both ways and both agree.

**Choices made (the developer may change them).** R steps through smart rotation's ranked facings rather than a
separate list of "variants" (a socket has one facing; the spin covers the rest). Face points and off-grid angles are
always freeform segments (a face point has no socket to link). A piece held to the ground (pinned or on a working
foundation) can't be the piece moved: pick it as the target. There is no weight limit beyond the merge limits.

## Point weld (board item pw01)

The developer's request: "click on the part that's going to move, and where you click is where the weld alignment will
originate from. Then you click on the target location and have a rotation option. And then weld, like the socketed
welded tool, but based on where you click." **Point weld** is the third mode on the welder's outer ring (socket weld
stays the default).

1. **The origin.** With the welder out, nothing in your hands, LMB on the piece to move (a loose body or a separate
   construct; not one held to the ground): the point under the crosshair on that face (on the 1 cm grid of the piece's
   main body) and the face's normal are the weld's origin and alignment. An orange dot, arrow and cross mark it.
2. **The target.** Aim at another body: a live ghost puts the origin on the point under the crosshair, its normal facing
   the target's (face to face), green or red with the reason on the look line (a green dot marks the target point). LMB
   picks it.
3. **Spin.** Mouse X (the view holds still) or the wheel turns the ghost about the target normal in 15 degree steps (a
   click on each 90), Shift free, Ctrl 5 degrees, with the socket weld's ring of ticks; R flips it half a turn. LMB welds.
   RMB steps back (3 to 2 to 1), Esc cancels; putting the welder away, another mode, or the piece or target changing
   under it (the picked parts' kind and place are stamped and checked every frame) clears it.

**How it welds.** It is the socket weld with a face point on both sides: the same pose (`SocketWeld.Pose`), rules and
refusal reasons (`SocketWeld.Check`: overlap with the construct, the map and players, the merge, joint and freeform
limits, the lock refusal), the same server checks (reach, ownership, decision 19's contested rule) and the same time
(survival: the piece's fit time, `EditKind.SocketWeld`; creative at once). **Sockets lined up**
(`PointWeld.Resolve`, worked out by the client's ghost and again by the server on the same numbers): where the
face-to-face pose puts a free socket of the clicked piece part within 2 cm of a free socket of the clicked target part,
facing it within about 3 degrees, it is that socket weld (so a multiple of 90 degrees onto a socket of the same kind
merges on the grid, linked); a wheel and a suspension whose free axle sockets come within 0.35 m, facing, go axle onto
axle, so the wheel rolls and springs. Otherwise the piece is a fixed-pose freeform segment at the clicked points. A
**joint's leaf**: a target point on the leaf's end of a joint part (the outer quarter along the leaf socket's facing, as
the freeform weld has it; `SocketWeld.OnLeaf`) hangs the piece on the joint (the socket weld's face points onto a leaf
now do the same), and a piece that would also weld to the joint's fixed side is refused ("that would lock the joint").
Drivetrain parts welded across a seam work as the freeform drivetrain fix has it (fd01).

**Network (protocol 107).** `CActKind.PointWeld` at the end of `CActKind`: u16 piece, u8 part, the point (its body
frame) and its facing, u16 construct, u8 part, the point (that part's segment rest frame) and its facing, u16 angle
(0.01 degree). The server resolves the sockets itself and runs the socket weld's path (`ConstructEdits.BeginPointWeld`;
the piece's point is kept in the hands as `SrcAt`/`SrcDir` for a timed weld). Code: `Source/Constructs/PointWeld.cs`,
`Source/App/Program.SandboxPointWeld.cs` (the input; the steps are a `SocketWeldFlow` whose source is a face point),
`SocketWeldRender` (markers and ghost).

**Self-test** "Point weld" (`SelfTestPointWeld.cs`), through the real input (`Program.SweepFrame` outside an ImGui frame,
`SweepDraw` in a real headless frame with error recovery): a loose panel's clicked point onto a frame block's face,
spun 30 degrees with the wheel (R to 210 and back), welds at the ghost: the point on the block's (1 cm), normals opposed
and the spin as asked (1 degree); clicked points on two sockets merge on the grid, linked; a panel that would overlap
the construct is red with the reason and LMB welds nothing; RMB, Esc, the welder put away, socket mode and the piece
removed all clear it; holding still in steps 2 and 3 allocates nothing; a real host and a separate client (lag, loss):
the client's weld lands at its ghost on both peers. Through the server path with the points the input would pick: a
panel on a motor hinge's leaf hangs on the joint and the lever turns it; a block on the leaf beside a wall on the fixed
side is refused (lock), without the wall welds; the buggy's four wheels welded back by points a few centimetres off
their hubs and the suspensions' axle faces roll, spring and steer like the wrench's; the engine welded back onto the
gearbox by clicked points drives the buggy like the blueprint's. "Interaction sweep" covers the mode too.

**Choices made (the developer may change them).** Point weld is a third ring option, not the default (the developer's
"change the default connection" was about the linking tool). Points snap to the 1 cm grid like the socket weld's face
points. Sockets "line up" within 2 cm and 3 degrees (an axle 0.35 m); only the two clicked parts' sockets are looked at.
The spin starts at 0 (smart rotation's guess is the socket weld's: there is no socket to rank).

## Scale: thousands of constructs

Board item dev-sandbox-construct-limits-cl01. The world's fixed caps (64 bodies, 1,200 parts, 128 joints, 512 wires,
chosen to keep the tick in budget when every loop walked every body) are gone. A world holds thousands of constructs,
wrecks and loose parts; what is at rest costs next to nothing; the per-construct caps are in
[Size classes](#size-classes-the-capital-parts).

- **Sleep.** A resting body sleeps (40 still ticks, as before). In a big world (from `ConstructWorld.LinearBelow`, 48
  bodies) the physics steps only a list made each tick (`ConstructPhysics.SimList`): the awake bodies, the held and
  changed ones, and whatever they may touch or are coupled to. A sleeping body is not integrated, not in the contact
  solve and not sent; a sleeping one with motors is only asked whether a motor wants to move (`WakeCheck`). Contact with
  a moving body, a tool, an edit, a blast, a logic or power change wakes it, as before.
- **Broadphase** (`Source/Constructs/ConstructBroadphase.cs`). A hashed uniform grid (8 m cells, 4,096 buckets) over the
  bodies' spheres in x and z, rebuilt lazily (the first query after a step, two passes, no allocation; bodies made or
  grown since go in a short list every query walks). Ray tests, player blocking, blasts, the physics' body pairs, cargo
  volume scans and bot obstacles ask it (`Near`, `NearRay`) for the bodies near them; small worlds walk the list exactly
  as before. A seeded check compares 200 rays through the grid with a walk over every body.
- **Network** (no wire change: protocol 100). In a big world each client gets its own pose packets
  (`ConstructNet.WritePosesFor`, Game `SandboxPoses`): the bodies that move or changed (and for 8 pose ticks after),
  every pose tick within 80 m of the client's player, every 2nd to 200 m, 4th to 400 m, 8th beyond, staggered by id; a
  few sleeping ones refreshed in turn; up to 6 packets a client a pose tick, the start turning with the tick so nobody is
  starved. A joiner gets the constructs **streamed**, 16 KB a tick (about 1 MB/s; `GameSandboxStream.cs`), and the stores,
  links and its inventory after the last one; small worlds keep the broadcast and the single burst.
- **Rendering** (`Source/Render/ConstructRenderCull.cs`). A construct out of view or past 700 m isn't drawn; one past
  140 m with 5 parts or more is drawn as one box over its bounds in its first part's colour. Near ones draw part by part.
  A client's blend skips sleeping bodies already where their last pose put them.
- **The host's safety cap.** Not a design cap: "Max constructs" (the lobby room's Match tab in Sandbox; default 10,000
  bodies, and 25 parts a body on average: 250,000 parts) only guards against runaway spam, and "Constructs per player"
  (default 12, "No limit" for a private game; lower it on a public server). Refusals say which limit and what it is.
  Joints and routed links in all scale with the body cap (4 and 8 a body). `Settings.SandboxMaxBodies` and
  `SandboxMaxPerPlayer` (`settings.txt`).
- **Save and load.** There is no world save file yet (blueprints hold one construct); the world's state
  travels whole only to a joiner, and "Construct scale" checks that every copy of 2,000 matches the host's.

Measured by the "Construct scale" self-test (seeded, a few ticks each; GitHub Actions runners, 2026-10-31; the Windows
column is the busiest tick's CPU time, which the test holds under the 8 ms budget). The numbers also come out as a notice
on a branch's CI run.

| World | Awake | Simulated a tick | Tick (wall, Linux CI) | Tick (CPU, Windows CI) | Allocated |
| --- | --- | --- | --- | --- | --- |
| 2,000 bodies, ~4,300 parts | ~195 | ~195 | 1.5 to 1.8 ms average (worst 1.9 to 2.5) | 2.0 to 2.2 ms worst (1.4 to 1.8 ms average, wall) | 0 |
| 5,000 constructs, 100,000 parts, + 300 blocks | ~300 | ~300 | 1.7 to 1.8 ms average (worst 1.9 to 2.1) | 1.7 to 2.2 ms worst (1.5 to 1.9 ms average, wall) | 0 |
| 2,000 bodies at rest | 0 | 0 | 0.14 ms | | 0 |

A joiner had all 2,000 bodies after 0.3 s over the loopback; queries looked at about 25 bodies (a ray down), 85 (a
40 m ray along a row) and 70 (a player's blocking) of 2,000.

### Choices made (the developer may change them)

- The grid's cell (8 m), the bands (80 / 200 / 400 m), the stream rate (16 KB a tick) and the LOD and cull distances
  (140 m, 700 m) are constants, not host settings.
- Worlds under 48 bodies keep every old code path exactly (the walk over the list, the pose broadcast, the join burst),
  so the existing tests and small games behave as they did.
- No protocol bump: the poses and the structure keep their format; a client takes several pose packets a tick.
- Far constructs are one box in the colour of their first part; instanced per-construct batches are left to the render
  preparation work (dev-perf-threading-mt01).
- Every construct's arrays are sized to it (the capital parts), so a world of small bodies costs less than the 44 KB a body it did.

