# Overview and playing

Covers the milestones' summary, how to play it, what a construct is, Creative or Survival, factions. Part of the construct guide: index in ../CONSTRUCTS.md.

The sandbox survival mode (board item dev-idea-constructs-cs01; the full design and the developer's 16 decisions are in
the project doc `claude/sandbox-construct-mode.md`) is being built in milestones. **Milestone 1 is the construct core,
proven on one small test map**: parts, tools in hand, ghost sockets, one rigid body per attached group, a working
drivetrain, a passive hinge, per-part damage with wrecks, and all of it server-authoritative over the network. There is no
large world, streaming, factions, enemies, points of interest, resources, scanning, terrain digging or construct weapons
yet. **Milestone 2 adds joints, logic and the drivetrain as parts** (decisions 3 and 4): motorised hinges, turntables,
pistons and steering pivots; logic parts wired with a linking tool; transmissions and differentials you can shoot;
passengers, riding on moving decks and run-over damage; and the yard's lift, turret, motorised gate and wired buggy to
test them on. Everything new is listed under [Milestone 2](self-tests-and-history.md#milestone-2-joints-logic-and-the-drivetrain). **Milestone 3**
(in parts) adds [salvage and the inventory](gathering-and-salvage.md#salvage-and-the-inventory-milestone-3): taking what survives off wrecks, wrecked parts (hauled to a [Recycler](gathering-and-salvage.md#salvage-haul-decision-31) for materials)
from what was destroyed, building from counted parts, and wrecks cleared away when left alone; and
[construct weapons](weapons.md#construct-weapons-milestone-3): guns put together on a construct from the weapon builder's parts or
as heavy guns, fixed or on turrets, fired by a gunner or by logic (sentries), fed from ammo boxes that cook off, with the
weapon builder's penetrating, explosive and incendiary rounds.

A Sandbox world is saved and loaded as a whole: docs/sandbox/WORLD-SAVES.md.

## Playing it

Host a game on **Construct Yard** (Sandbox pack) and pick **Sandbox** (a Dev-only mode, like the template). There is no ready-up and no countdown (`GameModeModule.StartsAtOnce`): the host's START puts everyone in the world at once, nobody has to ready up, and someone joining later drops straight in. The yard has a
buggy, a wired buggy (a passenger seat, a horn on the driver's fire key, headlamps on aim), an anchored gate, a gate on a
motorised hinge with a button, a lift, a turret on a turntable with a lever, a parts rack (the yellow shelf in the
north-west corner) and some loose parts.

| Key | What it does in Sandbox |
| --- | --- |
| 3 or 4 / 5 | Take out the **attach tool** / the **linking tool** (press again to put it away). 1 or 2 go back to your guns. (3 was the wrench's key, which went into the attach tool; 4 and 5 are the kill-streak keys elsewhere; Sandbox has no streaks.) |
| F | At the rack: take a part (press again for the next kind). On a seat: sit ([a seat controls nothing by itself](joints-and-logic.md#control-modules): one linked through a Driving Module drives). On a door, hinge, motorised hinge or piston: open or shut it (unless its wires run it, or it's locked). On a button, switch or lever: work it. On an Analog Lever: hold and move the mouse to drag it ([Logic](self-tests-and-history.md#logic)). On a loose part: pick it up. In a seat: get out. |
| Fire (hold) | With a tool out and a part in hand: fit the part on the ghost socket under the crosshair, or set it down on the surface you aim at as a **new construct** ([New constructs](building-and-tools.md#new-constructs-free-placement)). Letting go early stops it. |
| Arrow keys | Turn the ghost part 90 degrees as you see it ([Turning the ghost](#turning-the-ghost)): Left / Right about the vertical, Up / Down tip it towards / away from you, Shift + Left / Right roll it. The same on a socket and on a surface. |
| R | Put the ghost part back the default way round (on a socket: the first way that fits; on a surface: upright, facing your look). |
| F (attach tool out) | On a part that touches another construct: attach the two into one. |
| Aim (hold) | With a tool out: take the part under the crosshair off (into your hand; with your hands full, into the backpack if it fits there, else dropped where it was, with a HUD line). |
| T | Drop the part in your hand. |
| Tab | The **inventory** (below): any part into your hands, any blueprint in front of you, a tool out. Tab again or Esc closes it. Sandbox has no scoreboard. |
| Middle mouse (hold) | The **tool wheel** (below): flick to a tool, let go to take it out; let go in the middle to cancel. |
| W S A D, Space | Driving: throttle, reverse (brakes first), steering, brake. |
| Fire / Aim / R (seated) | Held keys the construct's **modules** and **input readers** read. A seat with a Turret Module fires with Fire; with several, Aim steps to the next turret. |
| Physics gun (tool wheel, north-east) | Hold Fire to grab and carry a construct, wreck or loose part; wheel: distance; F + mouse: turn it (Shift snaps); Aim: freeze it; R twice: unfreeze yours ([The physics gun](#the-physics-gun)). |
| Hands | (The tool wheel, south.) Fire takes the loose or stored part under the crosshair into your hands; Aim drops what they hold where you look. [Hands](#hands). |
| Linking tool | Routed links ([Routed links](power-and-links.md#routed-links-decision-20)): point the tool wheel at it for its **outer ring** (route links, delete / move node / re-route, highlight all; no link type: the port gives it, [Link type from the port](flight.md#link-type-from-the-port-at01); [The outer ring](#the-outer-ring-a-tools-options)). Fire on an output, then Fire on an input: the game routes it round the parts and a ghost shows the route (Fire for a node of your own on the way; a red node: hold Fire and scroll to adjust it; hold Left Shift for free routing, no port snap); Aim or Backspace takes the last node off, Esc cancels. Aim on a part with nothing routed: cut its data links. R on a logic part: its next setting (a preset). |
| Track tool | (The tool wheel, between the physics gun and the attach tool.) Fire wheels one after another round a loop, Fire on the first (or R) closes it: a track round them. On a track: R settings, Delete takes it off, Fire puts one shot off back on. [Tracks](drivetrain-and-engines.md#tracks). |
| Scoop | (A part.) Push its edge into the ground on an arm to dig; tip it to pour, into a store under it or onto the ground ([Scoops](gathering-and-salvage.md#scoops)). |
| Ground drill | (The tool wheel, between the remover and the linking tool.) Fire digs the aimed ground into carried dirt, stone and sand; Aim puts it back. Outer ring: stone, dirt or sand ([Sculpting](../TERRAIN-DIGGING.md#sculpting)). |
| Configure tool | (The tool wheel.) Fire on a part: its settings panel. Shift+Fire: select more parts of the same kind (changed together). Aim: clear the selection. [The configure tool](building-and-tools.md#the-configure-tool). |

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
- **A suspension's axle**: a suspension is turned for the wheel it will carry: axle level, across the vehicle and out from the
  centre plane, before its mount's usual turn and anything learned (`SmartRotation.AxleUp`, `SnapBuild.FilterRank`). A bracket whose
  mount is opposite its axle has no such turn on a top or bottom face: there it is a spring foot, and a wheel on it lies flat,
  doesn't roll, and the ghost says so.
- **Mirror symmetry** (`mirror`): if the same part sits at the mirrored spot across the centre plane (the plane through
  the frame part's centre, square to the vehicle's right), the turn that mirrors its turn (its face and its top mirrored)
  wins: the right wheel matches the left.
- **Repetition**: the same parts next to it (`neighbour`, by the share of them with that turn: a row of panels or lights
  lines up), and the turn the player last chose for this part (`lastChoice`), relative to the vehicle frame.
- **Corrections** (`learned`): when the player places a part turned away from the guess, that turn is remembered for the
  part, the socket kind and the side of the vehicle the socket faces (relative to the frame) and preferred next time.
  Free placement keeps its own.
- **Clearance**: a blocked turn is never listed; covering another part's free socket (`coveredSocket`, each, at most four;
  a socket the part meets with one of its own doesn't count, it joins) or a facing part's face right against another
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
Swap weapon, and the middle button was free everywhere; binds version 24 gave it a second binding, the grave accent key, for
laptops: Left Alt was the gantry camera's orbit, which is live while the wheel is) and thirteen segments appear around the middle of the screen,
clockwise from the top: **[Nudge tool](self-tests-and-history.md#the-nudge-tool)**, **physics gun** (it took the hoist's place, [below](#the-physics-gun)), **track tool**, **attach tool** (east), configure tool, **no-collide tool**, **hands** (south; [below](#hands); the guns are on its outer ring), **remover** ([below](building-and-tools.md#the-remover-tool-board-item-rt01)), **ground drill** ([Sculpting](../TERRAIN-DIGGING.md#sculpting)), **linking tool** (west), **paint tool** ([below](building-and-tools.md#the-paint-tool-board-item-pp01)), **texture paint tool** ([below](building-and-tools.md#the-texture-paint-tool)), **fuse tool** ([Fusing](building-and-tools.md#fusing)). Hands hang due south, the attach tool and linking tool due east and west (an odd wheel, after a player takes a tool off, turns half a step to keep hands south: `RadialMenu.Tilt`). A saved wheel keeps its own order (by name); a tool new since it was saved joins at its end
([The configure tool](building-and-tools.md#the-configure-tool) has no number key: the wheel or the inventory's Tools tab; a slot with no tool in it is greyed out with "coming later", and letting go on one says so and changes nothing). While the key is held the view stands still and the
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
shapes from lines and boxes: a torch with sparks, two terminals and a wire, an open hand, a hook on a
chain, three sliders, a handset with waves, a drill); `Source/App/Program.SandboxWheel.cs` hooks it into the frame.

Choices made for the wheel (the developer may change them): the middle mouse button, not Q (Q swaps weapons); the wheel
sits at the screen centre (the cursor is captured, so "around the cursor" is the centre); the tools still to come are
shown greyed rather than left off (eight segments read cleanly as a compass, and they show what is planned); it is
closed in a seat (seated keys belong to the construct's input readers); no stick support yet (no Steam Input in the game).

#### Your tool wheel

The wheel is the player's: **Settings > Tool wheel**, or in a match the Reload key while the wheel is open (a window; Esc closes it; `Source/UI/UiToolWheel.cs`). A picture of the wheel sits beside two lists. **On the wheel**, clockwise from the top: click a row or a segment, then Take off, Move earlier or Move later. **Off the wheel**: Add, and the other ways to that tool. Default wheel puts it all back.

- **Nothing is lost.** Every wheel tool has a tile in the Tab inventory's Tools tab (Hands too: `Constructs/Tools/Hands.json`, whose model is only that tile, since hands are never drawn as a tool); the Attach tool and the linking tool keep their keys. The guns on Hands' outer ring keep their number keys.
- **Any number works**, none to all. An empty wheel doesn't open: a tap of its key still does what a tap does, and held it says where the wheel is set. Odd counts tilt as ever (`RadialMenu.Tilt`), so segment n / 2 hangs straight down on any wheel.
- **Hands stays at the bottom**: taking a tool off or adding one turns the wheel to keep Hands south, if it was. Moving Hands yourself is allowed, and then it stays where you put it.
- **Every tool can sit on it.** The Selector and the [material painter](building-and-tools.md#the-material-painter) aren't on the default wheel: they are on the Off the wheel list to add, and a slot past `SandboxToolWheel.DefaultTools` joins a wheel only when you add it. Any tool the settings don't name, new since they were saved, shows on the wheel (a default wheel's tool) or on the Off list (any other): never neither.
- **A change is made after the screen is drawn** (`Ui.TwApply`): a button only notes what it asks for, so nothing later in the frame reads a list it has just shortened (2026-11-07: taking the last tool on the list off closed the game). The settings tab also catches and logs anything the screen throws; "Tool wheel settings" (`SelfTestToolWheelSettings.cs`) takes off, adds and moves every tool from several saved wheels.
- **Each tool's outer ring** is yours too: pick a tool on the list and its options show in order, with Move earlier, Move later and Default order. That order is what the ring draws and picks from (`SetOrder`, `tool_wheel_order`, by option label); labels it doesn't know are dropped, and options it doesn't name land at the end. A default order keeps the default in the middle of its arc; yours is as you set it. The options aren't hidden or removed.
- **Tools that used to be ring options** join a saved wheel like any new tool; a saved ring order that names one simply drops the label.
- **Saved** in settings.txt (`tool_wheel`, `tool_wheel_off`, by slot name; `tool_wheel=default` follows the game's default). A default wheel's tool in neither list is new since you saved, so it joins the wheel: taking a tool off is remembered, and a tool the game adds later isn't hidden. This PC only: nothing on the wire.
- **In code**: `SandboxToolWheel.Slot` names a tool and is the default wheel's order. `Menu` is rebuilt with one segment per tool (`SetLayout`), sharing each tool's item, so hints and options survive; a segment number is a slot only on the default wheel (`SlotAt`). The wheel takes up a change at its next key press (`SetKeys`).

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
(`RadialMenu.Item.Options`; a tool gets them through `IToolOptions` and `SandboxToolWheel.SetOptions`), so the attach tool's
attach / cut and the physics gun's modes can use it when they have modes, and a third tier for other tools (actuator
sizes, attach tool modes) works the same way (the self-tests give the attach tool slot a test set with a third tier).

Choices made for the outer ring (the developer may change them): the pipe styles first sat on the same ring as the other
types, then on the third tier above, and are gone with decision 28 (one pipe); the edit modes and highlight all are options too; the ring shows the moment a tool is pointed at
(no rest needed); 30 degree option segments; "Tools" is gone from the linking tool (the tool wheel always opens now).

### The yellow glow

A part glows yellow under the crosshair only when the player's hands can work it: F with any tool out, or a press with the
Hands tool (`SandboxAim.Touch`). A seat, a door or a part on a working hinge, a button, switch, lever, Analog Lever or
selector, a store or machine, a hose part, a grab handle, ladder or step plate, a stored part, a loose part to pick up. No
tool's own action counts (the attach tool, the linking tool and the rest light nothing, and a freeform weld is rigid, so
welded pieces are not "something to open"), but a seat still glows with a tool out, because F works with any tool. A tool's
refusal stays a red outline with its reason; `SandboxAim.CanAct` still answers what a tool can do, for the look line and
the actions.

The glow covers what hands work, not the whole part (`ConstructRender.DrawAimGlow`), in this order: a stored part's slot;
the boxes a part file marks `"use": true` (a lever's handle, a button's cap, a door's handle, a seat's cushion and back) plus
an Analog Lever's `"rig": "lever"` boxes, which follow the handle; on a panel hung on a hinge, the Door Handle fitted to it or a hand's width at its free edge ([Doors](big-systems.md#doors)); a machine's hopper and a bin's or tray's fill region
(`InFillMin/Max`, `FillMin/Max`); otherwise the whole part. Something loose always glows whole, since all of it comes up in
the hands. A loose part counts only within the pick-up's own reach: the server measures that to its centre of mass, so a big
piece aimed at from its far edge doesn't glow.

### Hands

`ConstructTool.Hands` (wire byte 12, `Source/Constructs/ConstructEditsHands.cs`, `Source/App/Program.SandboxHands.cs`), the tool wheel's
south slot. It takes things and puts them down; it never fits or removes a part (`ConstructBuild.ToolFits` is false for it, and
the Replacer refuses it), so fitting stays the attach tool's and the attach tool's.

**Switching tool puts the held part away** (the user, 2026-11-06, for parts stuck in the hand while building; `ConstructEdits.SetTool`, `KeepsHeld`, `PutAwayHeld`). However the tool changes (the wheel, a number key, a gun, a tile in the Tab inventory), the part goes back at once unless the new tool is the hands or the one that fits it: without those a part taken off a shelf could never be fitted. Staying on a tool keeps it. Picking a gun with a part in the hands puts it away even with no tool out, since a part in the hands blocks firing. In creative it simply goes. Otherwise it goes to a beacon's network in range, else the inventory's counts; a kind at its count limit takes `Give`'s path with the hands full: the backpack, else dropped in front of you. A magazine's rounds stay noted for the player as on any other way out of the hands. A client shows the change at once (`Game.SandboxToolAsked`, the same rule), and its own tool follows the server again a second after it last asked, so a tool the server set (a saved world's) shows.

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

- **Attach tool**: a gas torch with a blue bottle, the off hand under the barrel; while attaching it shakes, its tip glows and
  flickers, it throws **sparks**, and on a lit map it lights its surroundings with a flickering blue-white arc
  (`LightGrid.AddDynamic`, one per attaching player; unlit maps have the glow and sparks only).
- **Linking tool**: a wiring probe with a glowing screen and a reel of wire; while a wire is being run (yours: from the
  first click to the second) the reel turns and the probe throws a green **beam**.
- **Configure tool**: a handset with a screen, sliders and a dial; it is "in use" (its motion) while a settings panel is open.
- **Physics gun**: an amber beam projector held in both hands; while it holds something its coil spins, it shakes a
  little and its tip lights the map, and a curved beam runs to the held thing ([below](#the-physics-gun)).
- The **ground drill** (its bit spins while a stroke runs; [Sculpting](../TERRAIN-DIGGING.md#sculpting)).
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
  "name": "Attach tool",
  "tool": "attach tool",
  "description": "A gas attaching torch...",
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
| `tool` | `welder`, `linker`, `physgun`, `configure`, `paint`; or one still to come: `hoist`, `scanner`, `drill`. One file a tool. |
| `hold` | How it is turned in the hand (degrees: pitch, yaw, roll), about the grip. |
| `boxes` | Up to 24: `size` (and `at`) or `min`/`max`, each 2 mm to 60 cm a side, `color`; `"glow": true` draws it unlit and bright (flickering in use, a slow pulse otherwise); `"spin": true` turns it about the `spin` section's `axis` through its `pivot`. The boxes must touch into one piece. |
| `grip` / `support` | The strong hand (`at` within 15 cm of the origin) and the off hand (optional: without it the off hand rests): `at`, `rot` (degrees), `shape` (`wrap`, `under`, `cup`, `flat`). |
| `tip` | Where sparks, the light and the beam come from. |
| `use` | While it works: `motion` (`ratchet`: turns `amount` radians about `axis`; `jitter`: shakes `amount` metres; `push`: jabs forward `amount` metres; `sweep`: turns side to side; `none`) at `rate` Hz; `spin` / `idleSpin` (radians a second for the spin boxes); `sparks`, `light`, `beam` (colours) and `beamLength` (metres). |

A bad file is refused with the reason (`--check-construct-parts` lists the tools too) and that tool is drawn as a plain
grey block.

Choices made (the developer may change them): the attach tool and
scanner two-handed; a carried part goes in the off hand, scaled down (a frame block would hide the view at full size);
putting a tool away brings the gun up with its equip time before it can fire; the attach tool's light shows only on lit maps
(the dynamic lights live in the light grid); the tools still to come have models but stay greyed on the wheel.

### A tool's screen

The user: tooltips and hotkeys "onto a display located on each tool that the player can see, so that the info will be in-world but displayed in a more organic way". Every tool with a model that you hold has a small screen on a stalk beside it, showing what the HUD's tool box did. Code: `Source/Game/GameSandboxToolCard.cs` (the card), `Program.DrawToolCard` (the draw), `ToolDisplay` in `ConstructTools.cs`.

- **In a tool file:** `"display": { "mount": [x, y, z] }`, where the stalk meets the tool, with optional `size` and `rows` (0.17 by 0.098 m, 6 rows, about 17 characters a row). The screen stands in the same place on every tool, beside it, inboard and low, so it is found without looking: `ToolDisplay.HeldAt` from the grip, before the tool's `hold`. It turns to the eye. Its case and stalk are drawn with the tool, in third person too; the card itself is only the first person's, so other players see a dark screen.
- **The card:** the tool's name (a carried part's, when you carry one), `Mode:` where the tool has a ring (linking tool, paint tool, attach tool), then the keys, or a timed edit's words and a progress bar. The keys are the same cached lines the HUD showed, built by `SandboxKeys`, so a rebind shows. `PanelCard.Wrap` sets a "key: what, key: what" line out on the rows.
- **Long lists page.** Hints are written most useful first, so the first page has the keys that matter. Past the screen they turn a page every 4 s, with "1/2" in the title. No per-tool shortlist to keep in step.
- **In the view model's pass.** `DrawSandboxTool` draws it after the arms in camera space, with the arms' depth and the view model's projection, in its own `PanelRender.Begin`/`Flush`, so it moves with bob, sway, use motion and the equip drop. At 720p the text is about 20 pixels high. Nothing allocates (0 B in 114 frames of `DrawSandboxTool`) and all of it is local, so a client has the same.
- **What stayed on screen.** The look line and notices. A look line is about what the crosshair is on, not the tool: it changes as fast as you sweep, runs to 100 characters or more against 17 on the screen, and is read while looking at the thing, so it stays near the crosshair. The inventory, beacon and backpack lines are about you.
- **Hands, the gun and no screen.** Hands have no model, so their hints stay on the HUD, as do the gun's. `Game.SandboxToolCardShown` is true only on a frame the card went out, so any view with no tool drawn (third person, a model file refused) keeps the HUD's lines rather than losing them.

Choices made (the developer may change them): pages, not per-tool shortlists; the screen's place; the look line staying on screen; a card's only state colours are idle and working.

### Noclip

Creative worlds only (the map's `infiniteParts`; the same test as the physics gun and the remover). The flight is
`NoclipMove`, a branch of `World.Simulate`, so the server, the client's prediction and its replays run one step.

- **Leaving keeps the speed.** `MoveState.RideFree` at `NoclipMove.Flung` holds until the player lands, with almost no air
  steering (`FlungControl`), so a player can flick themselves at things.
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

**Who may be aboard.** The gun and the Nudge tool take a contraption with people on it when everyone in its seats or on its deck is friendly (`Game.SandboxFriendly`, `SandboxAboardRefusal`): sides first (`AreEnemies`: teams, friendly bots), never an enemy bot; people are a free-for-all to `AreEnemies`, and factions don't exist yet, so between two people it is the contested rule's answer: creative has no contest, and in survival the owner is the faction, so it is your own construct they are on. Anyone else aboard refuses with their name on the look line, and one who boards during a hold ends it with that reason. Seated players stay seated and are carried with the body. The gun wins over the seat: a driver's keys do nothing while it is held. Your own seat stays refused ("get out of the seat first"). A carriage or leaf beam still refuses anyone standing or sitting on the piece (`LeafRefusal`).

Board item dev-sandbox-physics-gun-pg01 and decision 19. A Sandbox tool (`ConstructTool.PhysicsGun`, its wire byte 8,
after the tools still to come) on the tool wheel's north-east segment and in the inventory's Tools tab, with its own held
model (`Constructs/Tools/PhysicsGun.json`: an amber projector with a spinning coil and three prongs, two-handed; its
coil spins and its tip lights the map while it holds something). The keys are the existing actions, so they rebind:

| Key | With the physics gun out |
| --- | --- |
| Fire (hold) | Grab the construct, wreck or loose part under the crosshair (30 m reach; 12 m in survival, more with [tool upgrades](../TOOL-UPGRADES.md)). Let go to drop it (it keeps its speed: a flick throws it). |
| Mouse wheel | While holding: pull it in or push it out (12% of the distance a notch, at least 25 cm; 1.2 m nearest). |
| Interact (F) held + mouse | While holding: turn it (about up, and about your right); the view stands still meanwhile. Sprint (Shift) snaps its WORLD turn to whole 45 degree steps about the world's axes, as gravity has them where you are (user, 2026-11-10: the world by default, so two pieces line up with each other): the deck of the construct you ride ("snap: deck"), else a gravity generator's or a round world's up there, else the map's axes ("snap: world"); another construct within 2 m only with `ConstructGrab.SnapToNearby` on ("snap: construct N"): a notch of mouse left/right is 45 degrees about up, up/down about the world horizontal axis nearest your right, and while Shift stays down the piece keeps its world turn as you turn the view. |
| Aim | While holding: **freeze** it where it is and let go. Not holding: freeze or unfreeze the piece of a mechanism you look at. |
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
- **Strength** (pr02; were 200 kN, 8 kN and 40 m/s2): 1.5 MN in creative (at most 120 m/s2, so a 15 t truck gets 100 m/s2), things up to 60 t (a tier 5 Foundry is 48 t); survival 150 kN, up to 5,000 kg where a player starts (user, 2026-11-10; Karl's 2026-11-08 1,500 kg; was 20 kN and 500 kg), raised per player by [tool upgrades](../TOOL-UPGRADES.md) (heavier: "too
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
- **A free joint on its own** (`ConstructGrab.AimLeaf`, `LeafJoint`, `DriveJoint`, `ConstructEdits.GrabJoint`): the beam on a Free Slider's carriage or a free rotation part's moving side, or on anything that hangs on one, moves that joint alone: along its rail between the stops, or about its axis inside any limits. The rest of the construct stays where it is, and a frozen or anchored construct stays frozen or anchored. On the rail or a free part's base the beam takes the whole construct, as before.
  - **What it carries goes with it**, at the beam's strength (the same acceleration as a body of that mass, plus its weight, so it hangs against gravity), and survival's cap (5,000 kg, or the player's upgraded lift) counts what the joint carries. It stops on the stops and on what it meets. The beam pushes against the player, not the base, so a free body isn't shoved back. Let go, it keeps its speed.
  - **A carriage with nothing on it** has no joint, so the first grab makes one (it stays, carrying nothing, and is drawn where it was left). Hanging a part on such a carriage puts it back at its rest, as for any empty joint.
  - **Refused, with the reason on the look line**: a Linear Actuator ("its motor holds it: use its Set signal"), a Shaft Linear Drive ("its lead screw holds it: turn its shaft"), a mixed rail a powered section runs, a Lock signal that is on, someone seated on it. What hangs on a powered rail is taken with the whole construct as before; when that is refused too, the look line gives the motor's or screw's reason.
  - **Aim** freezes the held piece and lets go ([Pieces of a mechanism](joints-and-logic.md#pieces-of-a-mechanism)). The wheel changes the distance; F + mouse does nothing.
  - **Through a chain**: the beam pulls on the held point through every joint behind it (three force-limited rows at the point, `ConstructPhysics.BeamRows`), so a pull on a pendulum moves the slider it hangs on too. The solve shares the pull by how easily each joint moves that way; stops, Lock signals and frozen pieces take no share. A motor in the chain holds up to its rating, then gives. The base doesn't move for it.
  - **Network** (protocol 137): `CActKind.Grab` carries the part and "on its moving side", the point in the joint's frame; the tail carries the held joint's segment. `CJoint.HeldFor` is renewed each tick by the beam, so a hold can't outlive it. The holder of a single free joint draws the angle their aim asks for plus a correction following the server (`Game.PredictJoint`, the same lead bounds as a held body); in a chain only the server knows how the pull shares out, so the holder draws its motion, as a watcher does. Every client draws rails and free rotation parts along a path between poses (`ConstructLinear.Follow`) instead of stepping at 20 Hz.

**Contested constructs (decision 19)**, survival only (a map with infinite parts off; creative is unrestricted): every
editing tool (the physics gun, the attach tool fitting and taking off, the attach tool's join, the linking tool's wires
and settings; a tool still to come calls the same helper, `ConstructGrab.Defended` / `ConstructEdits.Contested`) is
refused on someone else's construct while its owner (a player or a bot; a member of your [faction](#factions) counts
as you) is alive within `ConstructGrab.ContestRadius` (40 m, tunable) of it plus its own size, which covers being in it:
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
  view with the default light. A tile's dot is the tool it needs (orange attach tool, green linking tool), its
  badge how many you have, and hovering shows its name, tool, mass, health, armour, fitting time, how many you have and
  its description. Above the grid: what is in your hands, how many tiles are shown, and the **recent** row (small tiles
  of your last eight parts). Each tab comes back to the category it was on for as long as the game runs. Rows out of view
  cost one stretch of space, so listing all 1,500 tiles costs what a screenful does.
- **Favorites**: the row at the top of the right side. A tile's star (top left, on hover) or a right-click on any part tile adds it, and the same removes it. They go by part name in settings.txt (`sandbox_favorites`), per player (`Source/UI/UiSandboxFavorites.cs`).
- **Picking**: click a tile, or move the highlight with the arrow keys and press Enter. A part: the client asks the
  server for it by id (the rack's `CActKind.Rack` request with a part id, so the server checks it: an unknown id is
  refused) and takes out the tool it needs, so the ghost sockets, fitting and dropping work exactly as with a part from
  the rack. A blueprint: the server builds it standing on the ground in front of you, facing the way you look, far
  enough out not to land on you (`CActKind.Blueprint`; refused outside Sandbox). A tool (one in the game): it comes out
  (again: put away). In creative a pick replaces what the hands hold: parts are endless, so it just goes (it isn't dropped), and a physics-only pick empties the hands too as its body lands in front of you. The window closes after a pick unless "Keep open after picking" is ticked; Tab again or Esc closes it.
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
| Joints and Motion | Hinges, Motor hinges, Pistons, Linear, Turntables, Free rotation, Flexible joints, Hitches and chains, Cranes and lifts, Launch and docking |
| Power and Fuel | Fuel tanks, Fluid tanks, Fuel lines, Batteries, Alternators, Power plants |
| Flight | Lift cells, Burners |
| Weapons | Guns, Ammo boxes, Feed chutes, Bombs, Workbenches |
| Weapon parts | Receivers, Actions, Barrels, Muzzles, Feeds, Ammo, Grips, Stocks, Optics, Utility, Upgrades, Melee, Grenades |
| Production | Drills, Saws, Sawmills, Smelters, Foundries, Assemblers, Extrusion (Extruders, Wheel Formers, Die Frames), Components |
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
be made) its card shows a plain colour swatch instead. A failure is not final: a slot whose data throws gets a swatch
alone; a set-up or batch failure (a stale GL error is cleared first; an incomplete framebuffer; the first picture of a run
read back empty) is tried again three times, 2 s apart, before the swatches stay. Options > Regenerate icons starts over
(the atlas is GPU memory only, no icon file on disk). Each reason is one throttled `[icons]` line in callofslop.log, and a
window that has made no picture for 10 s says where it stands.

It is only in Sandbox, and a map can turn it off with `"partsMenu": false` in its `"sandbox"` section, as Survival in the
lobby does: the key then says why ("the parts menu is off on this map", "... in survival") and the server refuses both requests. The
window allocates nothing per frame while open (every tile's text is made once; the catalogue's key is taken once).

Editing always needs the right tool in hand (decision 2), and since decision 27 the tool follows the part's **function**:
the **attach tool** (it took the wrench's work, which is gone from the game) attaches and cuts free every **physics-only** piece (frames, panels, corners, decks and
foundations, and every part too heavy or bulky to carry: engines, big gearboxes, big wheels, building pieces), with its
three-step [socket attach](building-and-tools.md#socket-attach-board-item-sw01-decision-26), and adds and removes the small parts that
are items (seats, light wheels, suspensions, tanks, fuel lines, small gearboxes, hinges, doors, lamps, sensors, logic and
gun parts) to and from your hands; the **linking tool** does wires and logic settings. With a gun out
or bare hands you can only use things: sit, drive, open doors, pick up and drop loose parts. Fitting takes the part's
`attachSeconds` (attached parts 2.5 s, bolted 1 to 3 s), taking off 60% of that. The HUD panel shows what is in your hands,
what you look at, the edit's progress, refusals and, when driving, speed, fuel and why it won't go.

## What a construct is

- A **construct** is a set of parts joined by **links** (an attachment or a bolt between two sockets). Everything joined
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

### Rebuilds

Everything that follows from the parts (mass, centre of mass and inertia, the segments, the collider and the part tree,
wheels, the drive line, the circuit, guns, every system's list of its parts) is worked out by `Construct.Rebuild`. An edit
(a part added, linked, taken off, broken or split away) only marks the body `Dirty`; the first thing in the next tick that
needs it (the power step, the gun step, the physics step) rebuilds it once, so ten edits in a tick cost one rebuild. Code
that sets `Dirty = true` gets a full rebuild, as always.

A **big body** (`Construct.IncrementalMin`, 1,024 parts or more) also keeps a **journal** of its **plain** edits since its
last rebuild (`Source/Constructs/ConstructRebuild.cs`):

- **Plain parts.** A part type is plain (`PlainParts.Of`) when it is a frame or a panel whose file sets nothing any system
  looks for: every field of `ConstructPartDef` is compared with a new one, and only the fields that shape structure (mass,
  health, armour, boxes, struct sockets, looks, the inventory's words) may differ, so a field added later counts against
  plainness until it is listed there. A part is plain (`IsPlainPart`) when its type is, it is on the body's own segment,
  holds nothing, is in no merge, and no routed link ends on it or runs over it. Frame blocks, beams, plates and wedges are
  plain; everything else is a **special part**.
- **What the journal takes.** `ConstructWorld.AddPart` of a plain part, `AddLink` between two plain parts, and `RemovePart`
  of a plain part whose every neighbour is plain (a part breaking off under fire goes the same way), including a split
  whose cut-off pieces are all plain (they become wrecks of their own, each a full rebuild of its own small body). The body
  must have no freeform welds and no merged receivers. Anything else, before or after in the same tick, makes it a full
  rebuild.
- **What it does with it** (`RebuildPlain`). The mass, both first moments and the boxes' second moments about the body's
  origin are kept as sums in doubles (`SumParts`): a part that goes takes its own share off, a new part adds its own, and
  the centre of mass and the inertia about it follow from the sums (`FinishMass`, O(special parts): the loads and the cargo
  are added on top). The bounds keep a count of the parts on each face and are summed again only when the last part on a
  face goes. The sums are summed from the parts again every 256 plain rebuilds, so rounding never builds up. The collider
  and the part tree are renumbered through the journal's map and lose the leaves of the parts that went (their bounds stay
  as they were: a little loose, never short); new parts go into a small tree of their own beside the collider's big one
  (`ConstructCollider.AddParts`); either is built again when that isn't safe (a quarter gone, no room, built at another
  point of the journal). The circuit (the frame is the circuit) is flooded again only when a powered part went and its
  working neighbours don't meet round it within a short walk (`MeetsRound`, 512 parts), or a plain piece was cut off; a
  new part next to a powered one is powered and passes it on. The same short walk tells `RemovePart` that nothing was cut
  off, so a part off a big hull doesn't flood the whole body to look for a split.
- **The special parts.** Every system that looks for parts of its own kind (wheels, springs, the drive line's engine,
  plants, thrusters, cells, surfaces, rotors, stabilizers, racks, tanks, hydraulics, volumes, logic, scoops, rails, guns,
  modules, seats and roots) walks `Construct.Specials`, the list of special parts, not every part; a plain edit changes no
  special part, only the numbers. The list is made at every full rebuild and mapped at a plain one; between an edit and its
  rebuild `Specials` hands back every part, so code reading it early still sees everything. A part that matters to every
  system (any part can burn: the gun step's count of burning parts) is still counted over every part.
- **The drive line's walk** reads each part's links through the link index (`EdgesAt`, in the edge order a walk over every
  link would have met them) instead of every link for every part it reaches.
- **The debug check.** `Construct.RebuildCheck` (off; `CALLOFSLOP_REBUILD_CHECK=1` turns it on) follows every plain
  rebuild with a full one and compares mass, centre of mass, inertia, bounds, radius, the segments, the circuit, batteries,
  the drive line, wheels, root, guns, anchoring, the special parts, the collider's leaves (and that its trees find every
  leaf a box meets) and the part tree; a difference goes to `Construct.CheckLog`.

A smaller body rebuilds as it always did (its numbers, collider and contacts are what they were). The costs and what a split
still costs are in docs/perf/LIMITS.md, "Rebuilds". The journal and the plain-part rule are what fused units build on ([Fusing](building-and-tools.md#fusing), below): a unit's plain parts keep
their own share of the sums, and a full rebuild adds the unit's cached share instead of summing them.

### The drivetrain (it only works when connected)

(Since the drivetrain families the drive line can also run through shafts, gearboxes, chains, clutches and transfer cases
to differentials and hubs: [Drivetrain parts](drivetrain-and-engines.md#drivetrain-parts-the-families), below.) A construct drives when it has a
working **seat** linked through a [Driving Module](joints-and-logic.md#control-modules) (or a signal wired into its engine), a working **engine**
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
  ([Compound collision](network-and-scale.md#compound-collision), below): oriented box against box with a contact manifold, speculative
  contacts solved with warm-started sequential impulses and friction. Nothing has a bounding box as its shape: a U nests
  round a post, an L hooks a bar, a beam goes through a gap.
- **The parts beyond joints collide too** ([Joint collision](network-and-scale.md#joint-collision), below): against the map, other
  constructs and wrecks, with the joint solved together with those contacts.
- **Raycast wheels**: a spring-damper along each suspension (stiffness from the body's mass and the suspension's
  frequency), side grip, drive force shared by the touching wheels, brakes, a friction circle, and tyre forces applied
  most of the way up to the centre of mass (an arcade anti-roll bar) so a buggy turns without rolling over. Every tyre answers
  the body's motion as the tick began, so the order the wheels come in doesn't steer it. A vehicle whose wheels on the ground are all
  holding still (no drive, no driver's throttle, under 1.5 m/s, not towed or held by a physics gun, not on a deck) stands: what
  is left of its slide and yaw is taken off the body as one, out of the tyres' spare grip (`HoldStill`), so it doesn't creep
  down a slope and sleeps; past that grip it slides. Driven with the wheels straight, a vehicle on wheels holds its heading the same way (`HoldHeading`), since a centre of mass off the middle turns it a little with every push of the drive. Each wheel is also
  a cylinder against the world ([Wheels](drivetrain-and-engines.md#wheels-the-rim-and-the-cylinder)).
- **Joints** (reduced coordinates: each is one number on its body; the moving ones follow the [strength model](joints-and-logic.md#actuator-sizes-and-strength)): a passive hinge is damped, falls under gravity,
  is held inside its limits, latched open or shut by Interact, swung by a player walking into its leaf, and held still
  while its lock is on; the moving joints are driven by a motor (milestone 2, below).
- A body that stays still for 40 ticks sleeps and costs nothing until a hit, an edit, a driver or a nearby body wakes it
  (in a big world it is not even in the step's list: [Scale](network-and-scale.md#scale-thousands-of-constructs)).
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

## Factions

Players group into factions and share what they own (`Source/Constructs/Factions.cs`, the server's side
`Source/Game/GameSandboxFactions.cs`, the window `Source/UI/UiFactions.cs`). Open the **Faction window** with **F2**
(rebindable, binds version 21; Esc closes it).

- **Found, invite, join.** Anyone not in a faction can found one by naming it (2 to 24 ASCII characters, no two alike in any
  case); its founder leads it. The leader invites players who are here (not bots or the story's people); an invite shows in
  the invited player's window with Join and No thanks, and lasts the session. Joining another faction leaves the old one.
  A faction is **invite only** or **open** (anyone joins from the list); for an invite-only one the list has **Ask to join**,
  which tells its members who may invite, and they **Let in** or **Turn away** from the Invite list (an invite to someone who
  asked lets them in). A leader who leaves hands over to the highest rank who is here (else anyone in it); the last one out
  ends the faction. At most 31 factions.
- **Colour and tag.** Each faction has a colour and a tag of 1 to 5 letters or digits (no two factions alike), started from
  its name (a hue from its letters, its initials) and changed under **Colour, tag and joining**. Name tags over players' heads
  show `[TAG]` in the colour before the name (`UiNameTags.cs`; `FactionTable.TagLabel` is made when the tag changes, so a
  frame makes no text). The faction's name can't be changed: the story keeps a player faction's standing by its name.
- **Ranks** (`Constructs/FactionRanks.json`, `FactionRanks.cs`). A rank set is a ladder of 2 to 8 ranks, lowest first, the
  last the leader's, each with what it `may` do: `Dismantle` (take apart a mate's things with the remover or the attach tool;
  using, driving and building on them needs no rank), `Give` (give a construct away), `Invite` (and answer those asking),
  `Remove` and `Promote` (only members of a lower rank, and only to ranks below one's own), `Relations` and `Looks`. Player
  factions use the `Player` set: Recruit (joins at it: may use and build on what is shared, nothing more), Member, Officer,
  Leader. The leader can **Make leader** another member and steps down to the rank below. Sets are generic data, so a story
  faction's ladder (the Ministry's colony, later) is one more set. A refused file falls back to the same ladder built in.
- **Other factions.** Toward each other faction a faction is neutral, offers an alliance, or declares war
  (`FactionTable.Stance`): two factions are **allied** when both want it and **at war** when either says so. For now this is
  shown in the window and told to both sides, and the story hears of it (below); it changes no rule of who may use what.
- **The story's factions** (docs/story/STORY.md "Factions"). The window lists how each story faction (the IT Department...)
  stands with your faction, -100 to 100 in words, and a **Notorious** faction is marked in red: the server copies it from the
  story once a second (`GameStoryFactions.StoryStandingsTick`) and sends it in the table when it changes. Founding a faction
  is a `Unite` event, and alliances, wars and their ends `Ally`, `Feud` and `Truce` between the two groups, which the radio
  can tell (Story/Lines/Radio.json).
- **Sharing.** A faction mate's constructs and beacons count as your own (`Factions.Shares(owner, player)`, which every
  ownership check now asks instead of `Owner == player`): you may drive, build on, take apart, configure, grab, key-turn and
  remote-control them, a mate's damaged construct is not salvage to you, its beacon network serves you, and it is never
  "defended" against you. Ownership itself stays with one player, and so do the per-player construct cap and the parts that
  go back into the remover's own inventory.
- **Giving.** The window lists the constructs you own (nearest first) with a Give button per member: the construct becomes
  theirs (only its owner may give it, not a wreck or a loose part, and not past the receiver's construct cap). **Hand over
  what I hold** puts the part in your hands into a member's hands within 8 m (`ConstructEdits.Give`: their hands, else the
  backpack, else at their feet). A magazine or a hose stays with you.
- **Saved worlds.** The faction table is keyed by owner byte, the same byte `Construct.Owner` holds, so a member who leaves a
  saved world stays in their faction under their offline byte (still sharing what they own) and is back in it on return.
  The world file keeps the factions in a `FCTN` section and their colours, tags, joining, stances and ranks in `FCT2`
  (docs/sandbox/WORLD-SAVES.md); a world from before `FCT2` gets colours and tags from the names, and its members are Members. Without a saved world, leaving the game
  leaves the faction.
- **Network** (protocol 161, 200 for colours and ranks). The server sends the whole table (`Msg.Factions`: names, leaders,
  colours, tags, joining, Notorious, the story factions' standing, members with their names and ranks, invites, asks,
  stances, the story factions' names) when it changes and to each joiner; actions are `CActKind.Faction` with a `FactionOp`, refused with a notice. Each
  Game has its own table (`WorldContext` swaps `Factions.Now`).

Not done yet: factions for bots or the story's people (hiring them as crew: a member is an owner byte, so a story person
would need one), a faction's colour or flag on its constructs (a buildable banner part), rules that follow from alliances
and wars, and carrying a faction across free travel to another planetoid's game.

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
  constructs do. A rig starts frozen, upright, and is under the survival physics gun's starting 5,000 kg while its bin is near empty: carry
  it to a vein, hold the bit just over the ground, freeze it. A map's `"survivalStart": ["name", ...]` (blueprints or parts)
  replaces the list and also applies to a map that is survival by itself; `[]` gives nothing. Without it such a map gets no kit.
- **What a fresh game can reach** (choices made, the developer may change them): gather iron, stone, sand and wood; smelt, saw and
  found metal, glass, planks, cloth, steel, concrete, rubber and laminate; a tier 1 Assembler makes 312 parts, its own machines and
  more Fuel Tanks (fuel) among them. **Tier 2 is closed**: copper needs a tier 2 drill and smelter, those need Wiring Looms, and
  Looms need copper and a tier 2 Assembler. So Batteries, Engines, Alternators and Gensets come only from salvage. The rack hands out
  only what the abstract inventory holds, and survival salvage goes to the hands, so it stays empty. A map under 150 m half width has no
  deposits unless it places some (the Construct Yard): the lobby says so; the ground drill still gives stone and sand there.
