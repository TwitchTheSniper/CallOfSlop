# Constructs and the Sandbox mode: milestones 1 and 2

The sandbox survival mode (board item dev-idea-constructs-cs01; the full design and the developer's 16 decisions are in
the project doc `claude/sandbox-construct-mode.md`) is being built in milestones. **Milestone 1 is the construct core,
proven on one small test map**: parts, tools in hand, ghost sockets, one rigid body per attached group, a working
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

Host a game on **Construct Yard** (Sandbox pack) and pick **Sandbox** (a Dev-only mode, like the template). There is no ready-up and no countdown (`GameModeModule.StartsAtOnce`): the host's START puts everyone in the world at once, nobody has to ready up, and someone joining later drops straight in. The yard has a
buggy, a wired buggy (a passenger seat, a horn on the driver's fire key, headlamps on aim), an anchored gate, a gate on a
motorised hinge with a button, a lift, a turret on a turntable with a lever, a parts rack (the yellow shelf in the
north-west corner) and some loose parts.

| Key | What it does in Sandbox |
| --- | --- |
| 3 or 4 / 5 | Take out the **attach tool** / the **linking tool** (press again to put it away). 1 or 2 go back to your guns. (3 was the wrench's key, which went into the attach tool; 4 and 5 are the kill-streak keys elsewhere; Sandbox has no streaks.) |
| F | At the rack: take a part (press again for the next kind). On a seat: sit ([a seat controls nothing by itself](#control-modules): one linked through a Driving Module drives). On a door, hinge, motorised hinge or piston: open or shut it (unless its wires run it, or it's locked). On a button, switch or lever: work it. On an Analog Lever: hold and move the mouse to drag it ([Logic](#logic)). On a loose part: pick it up. In a seat: get out. |
| Fire (hold) | With a tool out and a part in hand: fit the part on the ghost socket under the crosshair, or set it down on the surface you aim at as a **new construct** ([New constructs](#new-constructs-free-placement)). Letting go early stops it. |
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
| Linking tool | Routed links ([Routed links](#routed-links-decision-20)): point the tool wheel at it for its **outer ring** (route links, delete / move node / re-route, highlight all; no link type: the port gives it, [Link type from the port](#link-type-from-the-port-at01); [The outer ring](#the-outer-ring-a-tools-options)). Fire on an output, then Fire on an input: the game routes it round the parts and a ghost shows the route (Fire for a node of your own on the way; a red node: hold Fire and scroll to adjust it; hold Left Shift for free routing, no port snap); Aim or Backspace takes the last node off, Esc cancels. Aim on a part with nothing routed: cut its data links. R on a logic part: its next setting (a preset). |
| Track tool | (The tool wheel, between the physics gun and the attach tool.) Fire wheels one after another round a loop, Fire on the first (or R) closes it: a track round them. On a track: R settings, Delete takes it off, Fire puts one shot off back on. [Tracks](#tracks). |
| Scoop | (A part.) Push its edge into the ground on an arm to dig; tip it to pour, into a store under it or onto the ground ([Scoops](#scoops)). |
| Ground drill | (The tool wheel, between the remover and the linking tool.) Fire digs the aimed ground into carried dirt, stone and sand; Aim puts it back. Outer ring: stone, dirt or sand ([Sculpting](TERRAIN-DIGGING.md#sculpting)). |
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
Swap weapon, and the middle button was free everywhere) and ten segments appear around the middle of the screen,
clockwise from the top: **[Nudge tool](#the-nudge-tool)**, **physics gun** (it took the hoist's place, [below](#the-physics-gun)), **track tool**, **attach tool**, configure tool, **ground drill** ([Sculpting](TERRAIN-DIGGING.md#sculpting)), **hands** ([below](#hands); the guns are on its outer ring), **remover** ([below](#the-remover-tool-board-item-rt01)), **linking tool**, **paint tool** ([below](#the-paint-tool-board-item-pp01)). Hands hang due south, the attach tool and linking tool just past east and west (an odd wheel, after a player takes a tool off, turns half a step to keep it so: `RadialMenu.Tilt`)
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
- **Every tool can sit on it.** The Selector isn't on the default wheel: it is on the Off the wheel list to add, and a slot past `SandboxToolWheel.DefaultTools` joins a wheel only when you add it.
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
an Analog Lever's `"rig": "lever"` boxes, which follow the handle; on a panel hung on a hinge, the Door Handle fitted to it or a hand's width at its free edge ([Doors](#doors)); a machine's hopper and a bin's or tray's fill region
(`InFillMin/Max`, `FillMin/Max`); otherwise the whole part. Something loose always glows whole, since all of it comes up in
the hands. A loose part counts only within the pick-up's own reach: the server measures that to its centre of mass, so a big
piece aimed at from its far edge doesn't glow.

### Hands

`ConstructTool.Hands` (wire byte 12, `Source/Constructs/ConstructEditsHands.cs`, `Source/App/Program.SandboxHands.cs`), the tool wheel's
south slot. It takes things and puts them down; it never fits or removes a part (`ConstructBuild.ToolFits` is false for it, and
the Replacer refuses it), so fitting stays the attach tool's and the attach tool's. What the hands hold stays there when the tool changes.

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
| Fire (hold) | Grab the construct, wreck or loose part under the crosshair (30 m reach; 12 m in survival). Let go to drop it (it keeps its speed: a flick throws it). |
| Mouse wheel | While holding: pull it in or push it out (12% of the distance a notch, at least 25 cm; 1.2 m nearest). |
| Interact (F) held + mouse | While holding: turn it (about up, and about your right); the view stands still meanwhile. Sprint (Shift) snaps its WORLD turn to whole 45 degree steps about the world's axes (or those of another construct within 2 m; the key line says "snap: world" or "snap: construct N"): a notch of mouse left/right is 45 degrees about up, up/down about the world horizontal axis nearest your right, and while Shift stays down the piece keeps its world turn as you turn the view. |
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
- **A free joint on its own** (`ConstructGrab.AimLeaf`, `LeafJoint`, `DriveJoint`, `ConstructEdits.GrabJoint`): the beam on a Free Slider's carriage or a free rotation part's moving side, or on anything that hangs on one, moves that joint alone: along its rail between the stops, or about its axis inside any limits. The rest of the construct stays where it is, and a frozen or anchored construct stays frozen or anchored. On the rail or a free part's base the beam takes the whole construct, as before.
  - **What it carries goes with it**, at the beam's strength (the same acceleration as a body of that mass, plus its weight, so it hangs against gravity), and survival's 500 kg cap counts what the joint carries. It stops on the stops and on what it meets. The beam pushes against the player, not the base, so a free body isn't shoved back. Let go, it keeps its speed.
  - **A carriage with nothing on it** has no joint, so the first grab makes one (it stays, carrying nothing, and is drawn where it was left). Hanging a part on such a carriage puts it back at its rest, as for any empty joint.
  - **Refused, with the reason on the look line**: a Linear Actuator ("its motor holds it: use its Set signal"), a Shaft Linear Drive ("its lead screw holds it: turn its shaft"), a mixed rail a powered section runs, a Lock signal that is on, someone seated on it. What hangs on a powered rail is taken with the whole construct as before; when that is refused too, the look line gives the motor's or screw's reason.
  - **Aim** freezes the held piece and lets go ([Pieces of a mechanism](#pieces-of-a-mechanism)). The wheel changes the distance; F + mouse does nothing.
  - **Through a chain**: the beam pulls on the held point through every joint behind it (three force-limited rows at the point, `ConstructPhysics.BeamRows`), so a pull on a pendulum moves the slider it hangs on too. The solve shares the pull by how easily each joint moves that way; stops, Lock signals and frozen pieces take no share. A motor in the chain holds up to its rating, then gives. The base doesn't move for it.
  - **Network** (protocol 137): `CActKind.Grab` carries the part and "on its moving side", the point in the joint's frame; the tail carries the held joint's segment. `CJoint.HeldFor` is renewed each tick by the beam, so a hold can't outlive it. The holder of a single free joint draws the angle their aim asks for plus a correction following the server (`Game.PredictJoint`, the same lead bounds as a held body); in a chain only the server knows how the pull shares out, so the holder draws its motion, as a watcher does. Every client draws rails and free rotation parts along a path between poses (`ConstructLinear.Follow`) instead of stepping at 20 Hz.

**Contested constructs (decision 19)**, survival only (a map with infinite parts off; creative is unrestricted): every
editing tool (the physics gun, the attach tool fitting and taking off, the attach tool's join, the linking tool's wires
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
be made) its card shows a plain colour swatch instead.

It is only in Sandbox, and a map can turn it off with `"partsMenu": false` in its `"sandbox"` section, as Survival in the
lobby does: the key then says why ("the parts menu is off on this map", "... in survival") and the server refuses both requests. The
window allocates nothing per frame while open (every tile's text is made once; the catalogue's key is taken once).

Editing always needs the right tool in hand (decision 2), and since decision 27 the tool follows the part's **function**:
the **attach tool** (it took the wrench's work, which is gone from the game) attaches and cuts free every **physics-only** piece (frames, panels, corners, decks and
foundations, and every part too heavy or bulky to carry: engines, big gearboxes, big wheels, building pieces), with its
three-step [socket attach](#socket-attach-board-item-sw01-decision-26), and adds and removes the small parts that
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

### The drivetrain (it only works when connected)

(Since the drivetrain families the drive line can also run through shafts, gearboxes, chains, clutches and transfer cases
to differentials and hubs: [Drivetrain parts](#drivetrain-parts-the-families), below.) A construct drives when it has a
working **seat** linked through a [Driving Module](#control-modules) (or a signal wired into its engine), a working **engine**
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
  most of the way up to the centre of mass (an arcade anti-roll bar) so a buggy turns without rolling over. Every tyre answers
  the body's motion as the tick began, so the order the wheels come in doesn't steer it. A vehicle whose wheels on the ground are all
  holding still (no drive, no driver's throttle, under 1.5 m/s, not towed or held by a physics gun, not on a deck) stands: what
  is left of its slide and yaw is taken off the body as one, out of the tyres' spare grip (`HoldStill`), so it doesn't creep
  down a slope and sleeps; past that grip it slides. Driven with the wheels straight, a vehicle on wheels holds its heading the same way (`HoldHeading`), since a centre of mass off the middle turns it a little with every push of the drive. Each wheel is also
  a cylinder against the world ([Wheels](#wheels-the-rim-and-the-cylinder)).
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
| `creative` | `true`: set in creative worlds only. The server refuses it in survival, the panel shows it read-only and a survival world leaves a blueprint's value out. |
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
| `latched`, `hold` | Button, Switch | Push on, push off; or on for `hold` seconds after a press |
| `value`, `snap`, `low`, `high`, `spring`, `rest` | Analog Lever | Where the handle stands; the step it clicks through; what it puts out at 0 and 1; goes back to `rest` when let go ([The Analog Lever](#the-analog-lever)) |
| `startOn` | Switch, Toggle | A new one starts on |
| `radius` | Proximity Sensor | Its range (a sentry turret aims within it too) |
| `units`, `threshold` | Speed Sensor | m/s, km/h or mph; above 0, on/off at that speed instead of the number |
| `colour`, `brightness`, `blink` | Lamp | Its glow's colour and brightness; flashes a second while lit |
| `pitch`, `volume` | Horn | Its beep |
| `min`, `max`, `speed`, `strength` | Piston, Motor Hinge, Hinge (limits), Turntable (speed, strength) | Limits (degrees; a piston's metres) inside the part's own, speed, force or torque |
| `pushable` | Passive hinges | A player walking into what hangs on it swings it (on by default). [Doors](#doors) |
| `strength` | Brake | 100%: the wheels lock; less: they drag (`jFwd` blended towards a stop) |
| `stiffness`, `damping` | Suspension (a plain spring) | Spring frequency (Hz) and damping ratio |
| `friction`, `dirtGrip`, `sideGrip`, `rolling`, `bounciness` | Every wheel, track and tyre, without a line in its part file | The tyre: [Wheels](#wheels-the-rim-and-the-cylinder) |
| `fluid`, `fill` | Fluid Tank | What it holds (none, or any fluid: the options are filled in from the game's list) and how full it is, %. Creative only ([Fluid tanks](#fluid-tanks)) |
| `setting` | Input Reader, gearboxes, selectors, differentials, transfer cases, adjustable suspensions | Their own choice, above |

Values live on the part (`CPart.Cfg`, eight floats inline, so they move with the part when parts are renumbered or a
construct splits; a colour is 0xRRGGBB, exact in a float), are read through `ConstructConfig.Get` (a table lookup, no
allocation), start at the defaults when a part is fitted, and are clamped wherever they are set. A part taken off,
stored or picked up loses its settings (it goes back to the inventory as a kind).

### Reach

A part's numbers are in the frame of its segment, and a freeform-welded part keeps the numbers of the piece it came from, so a point on a part is `Construct.SegToWorld`, never `ToWorld`. `ConstructEdits.ReachPoint` (take off, use), `AttachReach` (fitting a part: the server's check and the client's ghost, which turns red and says "too far away" rather than offer what the click is refused) and `WeldReach` (the welds) are the ways to measure it, and take the eye from the world, in or out of a ride frame.

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
| `role` | `frame`, `panel`, `seat`, `wheel`, `suspension`, `engine`, `fueltank`, `fluidtank`, `fuelline`, `hinge`, `door`, `foundation`; milestone 2: `joint`, `transmission`, `differential`, `logic`; milestone 3: `gun`, `gunpart`, `ammobox`, `feedchute` ([Construct weapons](#construct-weapons-milestone-3)); `generator`, `thruster` ([Big drive](#big-drive)). |
| `tool` | Optional, `wrench` or `welder`: only a hint that the part is structural (`welder`: physics-only; `wrench`, or none: an item unless it is heavy or bulky). The attach tool fits every part whichever it says, and the words stay so part files need no change. Old files load unchanged. |
| `attachSeconds` | Time to fit (default 1.5 an item, 2.5 a structural part); removing takes 60% of it. |
| `mass`, `health`, `armour` | kg, hit points, flat damage taken off each hit. |
| `material`, `friction` | `steel`, `rubber`, `concrete`...; friction defaults from the material (rubber 1.0, else 0.6). |
| `onBreak` | `detach` (breaks off) or `disable` (stays, stops working; the default for engines, tanks, seats, transmissions and differentials). |
| `boxes` | Up to 32: `min`/`max`, or `size` (and `at`), each with an optional `color`. `"use": true` marks what a hand works, all the yellow glow covers ([The yellow glow](#the-yellow-glow)). `"detail": true` boxes are drawn only (no bounds, mass, collision or hit area) and may stick out. At least one box is solid. `"hidden": true`: a solid box that is never drawn. A detail box may have a `"shape"` and its settings ([Structural parts](#structural-parts-the-scale-chart)). `"paintable": false` (or `true`) says whether the paint tool's colour replaces the box's own ([The paint tool](#the-paint-tool-board-item-pp01)); the default is yes, except glass and a wheel's tyre (its solid boxes). `"side": "base"` or `"leaf"` tags a joint part's box with the side it belongs to ([Joint sides](#joint-sides-board-item-js01)); every joint part must tag both. |
| `sockets` | Up to 40 attachment points: `at` (on a face of the solid boxes' bounds, or on a face of one solid box facing out of it: a door's jamb inside an opening, a window's sill), `dir` (`+x -x +y -y +z -z`, out of that face), `kind` (`struct` default, `axle`: a wheel onto a suspension, `leaf`: a door onto a hinge; a joint's leaf also takes any `struct` socket, [Doors](#doors)). Two sockets mate when their kinds match and they face each other. |
| `engine` | `torque` (N m at the peak), `maxRpm`, `idleRpm`, `peakRpm`, `curve` (`petrol`, `diesel`, `electric`, `turbine`, `flat`, `steam`), `fuel` (`fuel`, `electric`, `none`), `burnRate` (litres a second at full throttle), `starter` (needs a battery), `starterKJ`, `spool` (seconds), `noise` (0 to 1). [Drivetrain parts](#drivetrain-parts-the-families). |
| `wheel` | `radius`, `grip`, `offroad` (times grip on dirt), `side` (times grip sideways), `roll` (rolling resistance), `drive` (false: a caster), `flat` (goes flat when wrecked), `track` (`length`, `rays`). Needs an `axle` socket. |
| `suspension` | `travel` (m), `frequency` (Hz, at most 6), `damping` (ratio), `maxLoad` (kg a wheel), `active` (a wired signal sets its height), `presets` (`label`, `frequency`, `damping`, `hold`), `ride` (a gas strut), `slew`, `motor` (a hub motor), `path` (the wheel's way through its travel: [Suspension types](#suspension-types)). Needs an `axle` socket. |
| `transmission` | `kind` (`manual`, `sequential`, `automatic`, `cvt`, `reversing`), `gears` (reductions, lowest gear first), `labels`, `gear`, `reverse`, `shift` (s), `efficiency`; a CVT's `range`. |
| `differential` | `kind` (`open`, `locked`, `limited`, `lockable`), `ratio` (the final drive), `efficiency`. |
| `ports` | The part's link ports by function (board item np01, [Named link ports](#named-link-ports-np01)): a list of `{name, type, dir, socket or at + face, fluid, noEffect}`. |
| `drive` | A transfer part (role `transfer`): `kind` (`shaft`, `ujoint`, `cvjoint`, `gearbox`, `chain`, `belt`, `clutch`, `transfercase`, `pto`, `hub`, `stub`), `ratio`, `efficiency`, `ports`, `maxAngle`, `maxTorque`, `front`, `setting`. |
| `fuelTank` | `capacity` (litres; a tank comes full). |
| `fluidTank` | `capacity` (litres; it comes empty) and `flow` (litres a second through its pipes at most): [Fluid tanks](#fluid-tanks). |
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
A blueprint may say `"tow": "Name"`: spawning it also spawns that blueprint behind it with its first Tow Eye on this one's first Tow Hitch, coupled (`ConstructBuild.BuildOnGround`). A blueprint may say `"group": "Bandit"`: the Blueprints tab shows it under that group instead of by what it holds. The **Bandit Technicals** (`Constructs/Blueprints/BanditTechnical*.json`, parts in `Constructs/Parts/Bandit` from `tools/constructs/bandit_parts.py`, body parts in `Constructs/Parts/Body` and `Constructs/Parts/Hull` from `tools/constructs/body_parts.py` and `hull_parts.py`) are written by the vehicle kit (docs/vehiclekit/VEHICLEKIT.md): change the generator, not the files.
A blueprint may say `"loot": [{"step": n, "table": "bandit"}]`: that step's container fills from the table when the blueprint is spawned ([Loot](#loot)).
The **RV** (96 build steps, 90 parts after shaft chains migrate, about 2.65 t, 8 wires, a fuel pipe) is a walk-in motorhome: a driven, sprung, steered chassis, a cab, a hinged door with a boarding ladder, a dinette, a bunk over a cargo bay, lamps on switches and a motorised awning. It is generated by `tools/constructs/make_rv.py` and checked by the "RV blueprint" self-test.

### Loot

`Constructs/Loot.json` (`ConstructLoot.cs`) holds a table per faction: `maxTier` (the Assembler tier of the most advanced part it may hold, 2 for bandits: Scrap and Workshop), `rarities` (for weapon builder parts), `rolls` (picks a container gets), `empty` (the chance it holds nothing) and `items` (part, weight, count). What breaks those rules, a physics-only part above all, since slots hold items, is left out and listed by `--check-construct-parts`.

**When it rolls.** In `ConstructBuild.BuildOnGround`, so on the server and on every spawn: the Tab inventory, a map's constructs, a survival world's. Not in a scratch build, a stamp or a clip. Rolls come from a seed the world steps per spawn, nothing to do with the simulation, so they touch no seeded digest. What rolls goes into the container's ordinary slots through `ConstructStorage.Check`, and clients get it as any store change; no wire change.

**Saves.** From then on it is stored parts: saved with the world, taken by hand, spilled when the container goes. A load restores what the store held and rolls nothing. A player's own blueprint of such a vehicle keeps no loot and no key.

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

A part is **physics-only** when it is fitted with the attach tool, when it is heavier than the hand-carry limit
(`ConstructParts.HandCarryLimit`, 40 kg: the backpack's limit), or when its file says `"physicsOnly": true` (light
but bulky pieces: `tools/constructs/building_parts.py` sets it for the 1.25 m wood door, the fence gate, the wood
railings, the 2 m wood beam, the tower brace and the 1 m window glass). The rule reads the attach tool hint and the mass, never a
list, so every new structural or heavy part follows by itself. Such a part is never an item:

- It can't be held, stowed in the backpack, set into a storage slot or picked up; each refusal says why ("the Frame
  Block is physics-only (structural): it can't go in the backpack - move it with the physics gun or carry it in a cargo
  volume").
- The give rule (`ConstructEdits.Give`) drops it as a body where it was, so every path follows it (salvage, merge
  pop-offs, storage). Taking one off a construct cuts it free as a body at its place, moving as the construct moved.
- The creative inventory (Tab) and the parts rack spawn it as a body a short way in front of you at eye level (clear of
  the map, constructs and players; it then falls and settles). With the physics gun out, the gun holds it. An item
  still comes into your hands, ready to fit.
- It is built onto things with the attach tool (decision 27): the three-step [socket attach](#socket-attach-board-item-sw01-decision-26)
  (pick its socket, pick where it goes, spin it), or by moving it into place with the physics gun and joining it (F).
- The inventory tile has a small boxed corner mark and its tooltip says "PHYSICS ONLY (structural)" or "item"; the look
  text says "physics only" too.
- What a save held in hands, backpacks or storage that is physics-only drops into the world as bodies
  (`ConstructEdits.DropPhysicsOnly`; world saving isn't built yet, so the server runs it at every match start).
- **Creative** (infinite parts on): an item's fit or removal is instant; a physics-only piece keeps its time. Survival and counted parts keep the timed edits.

### Cargo volumes

The **Cargo Volume** part (`Constructs/Parts/Storage/CargoVolume.json`, fitted as an item, a logic part of kind `cargo`)
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

### No-collide pairs

The **No-collide tool** (on the default wheel and in Tab > Tools; `ConstructTool.NoCollide`, no ring): click a piece, then another, and that pair passes through each other. Click the pair
again to let them collide. Clicks pick as Freeform attach does; RMB or a click on nothing drops the first pick. While the tool is
out every pair is drawn as a dashed line through everything, with a tick at each end, and the second pick's look line says the
state (`Frame Block and Frame Beam: no-collide - LMB: let them collide`).

- **What a click means.** A click takes the whole rigid piece under the crosshair: a plain construct, or one moving segment of a
  mechanism. With Shift held it takes that one part. The two ends are chosen apart, so a part can pass through a whole piece.
- **Two bodies or one.** Parts of one construct never collide, so a pair within one construct does nothing until the two come
  apart (a cut seam, a split), and then they still don't meet. Two parts of one rigid piece are refused.
- **The table.** `ConstructWorld.NoCollides` (ConstructNoCollide.cs), at most 1,024 pairs, each end a part tag, like a
  connector's. A hash on the two bodies is remade only when the list changes; a body with a pair carries the world's stamp, so
  a pair of bodies with none costs two integer compares in `PairContacts` and nothing allocates. Both-whole pairs skip at the
  group, others at the leaf. `NewOverlap` (the physics gun's held sweep) and `GroundRay` (a wheel or foot on a deck) ask too.
- **What ends a pair.** The part it names going. That holds for a whole-piece end as well: the part clicked is the anchor, so a
  piece that loses that part loses the pair.
- **Taking it off while they overlap.** Nothing pushes them apart gently: the contacts take over from wherever they are, and one piece
  fully inside another can stay there.
- **Carried by** saves (section `NoCollide`, older saves have none), joiners and changes (`CBodyKind.NoCollide`, the whole list, at most
  10 times a second), a copy, and blueprints (`"noCollide": [{ "a": 0, "b": 2, "wholeA": true, "wholeB": true }]`, a step is a part
  index). A blueprint holds one construct, so a pair between two constructs isn't in one.
- The solve is the server's; a client holds the list for the marker and the look line.

### Joints on the wire

A pose carries every joint's angle with the body's server tick. A client keeps them with the pose (`Construct.NetHistAngle`, as `NetHistPos`) and draws each joint at the same tick as the body, through the same cubic (`ConstructNet.SampleJoints`; a free joint goes the short way round). A joint set as its pose arrived was a round trip's delay ahead of a body drawn on the ticks, so a moving chain came apart from its body and from itself. Without a render clock (a pose with no tick), the old path stands.

### Compound collision

A construct's collider is its real shape. `Source/Constructs/ConstructCollider.cs` (the boxes and trees),
`ConstructNarrow.cs` (the pair tests), `ConstructPhysicsContacts.cs` (who meets what, the points, warm starting),
`ConvexHull.cs` (shaped parts).

- **The collider.** Every solid box of every part is a leaf (`SolidCount`/`SolidBox`, so a merged receiver is its one big
  box). Leaves are grouped by **rigid group**: the body and its freeform attachments are group 0, each real joint's parts another
  (`Construct.MoveSeg`). Each group has a bounding volume tree in its own frame, so a joint moving costs nothing: the frame
  moves and the tree with it. A freeform-attached box is a leaf turned in its group. The tree is built on first use after the
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
  top (`ConstructNarrow.Ledge`). A crate rides over a 3 cm kerb; a deck of many plates has no joins.
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
- **Wheels** stand on rays and are a cylinder to everything else ([Wheels](#wheels-the-rim-and-the-cylinder)): one moving
  leaf per wheel, and a turning wheel's tread carries what it touches. A track unit stays rays and boxes that only meet
  something loose. A wheel with no suspension is a plain solid part.
- **Couplings.** A coupling's hitch parts don't collide with each other's construct, and points within 0.7 m of a hitch are
  dropped, as before.
- **Self-collision.** None: parts of one construct never collide with each other (joint limits keep the built ones apart).
- **No-collide pairs.** The no-collide tool ([No-collide pairs](#no-collide-pairs), below) takes a pair of pieces or parts out of these contacts.
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
| A wheel on a suspension | a sixteen-sided prism at the hub ([Wheels](#wheels-the-rim-and-the-cylinder)) |
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

## Doors

A door is whatever hangs on a hinge, not a kind of part. F on any part on a passive hinge's leaf, or a motor hinge no wire runs, opens or shuts it (`ConstructEdits.Use`), so a door can be any size and any material. `Source/Constructs/ConstructDoor.cs` holds what the player sees of that; the parts come from `tools/constructs/door_parts.py`.

- **Hanging anything.** A joint's leaf takes a door's `leaf` socket or a plain `struct` socket (`ConstructBuild.KindsMate`, used by the wrench's Place, the snap build, the socket weld and the vehicle kit), on a passive hinge as on a motor hinge. So a Panel, a plate or an Extruder piece goes on any hinge: an item with the wrench's path, a physics-only piece by a socket, point or freeform weld onto the leaf. An Extruder plate has edge sockets only at its stations (about every metre), so to hang one centred on its hinge use a length with a station at the middle (an odd number of metres) or a freeform weld.
- **The Door Hinge** (0.5 m) and **Long Door Hinge** (2 m) fit in a door's 2 cm gap: base socket on one jamb, leaf socket facing the door. The pivot is on the door's outer face (2 cm off the hinge's middle, towards its own -z), so the door swings out past the jamb and only the knuckles, 1 cm proud, show outside. They open one way: limits 0 to 100 degrees, towards the hinge's -z; turn the hinge end over to swing the other way. A player walking into a shut door from that side swings it, and from the other side is held. The old **Hinge** stays for what is built on it: 20 cm wide and proud of a wall, it suits gates.
- **The flush doors** (Steel and Wood, 4 cm thick) are named for their opening: a Flush Door 1.5x2.25 m is 2 cm smaller at each edge, with the Door Hinge in the gap on one side. The openings: 1.5x2.25 m a Door Wall, Door Corner or bunker (their `hinge` sockets); 0.7x1.8 m a Door Frame 1x2 m (its jamb is off the socket grid, so its hinge goes on with a freeform weld); 1.25x2.35 m a vehicle kit room; 1x2 m and 1.25x2 m for openings a player cuts. Each hangs from the middle of its edge, so a hinge at the opening's mid-height centres it. Steel weighs 43 kg/m2, so only the small ones are items; wood is physics-only, as the 1.25 m wood door is. The Two-Storey House, Bunker and Small Outpost blueprints use them.
- **The handle.** The **Door Handle** is a small plain part with a `"use": true` grip; fit it to a panel and that is what glows. A panel with none glows a hand's width at the edge away from the hinge, halfway along the hinge (`ConstructDoor.UseArea`). Both are only what lights: as on a Door, F works anywhere on the leaf. A beam or a frame on an unwired hinge counts the same way; engines, seats and the like still glow whole.
- **Pushing it open.** A hinge's **Opens when pushed** setting (`pushable`, on by default; every passive hinge: Hinge, Door Hinge, Long Door Hinge, Gate Hinges) says whether a player walking into what hangs on it swings it. The configure tool aimed at a door opens its hinge's panel, because a part with no settings of its own that swings on a joint is set on that joint (`ConstructDoor.ConfigPart`). A plain panel has its own settings, so aim at the hinge for one of those. A wire into the hinge's **Push** port wins while it is connected: high (0.5 and up) lets a push swing it, low doesn't, whatever the setting says; take the wire off and the setting is back (`ConstructDoor.PushOpens`). Only the server reads it, in `ConstructWorld.PushJoint`. F, a hinge's Lock port and what a motor does are untouched, and off doesn't stop a wall or a vehicle hitting the door.
- **The look line** on any of these: `Steel Flush Door 1.25x2.35 m: shut - F: open` (`ConstructDoor.Look`).
- **Nothing on the wire changes.** `CJoint.Open` and the joint's angle already go with every body; a hung panel is an ordinary segment in saves and blueprints.

Choices made (the developer may change them): a 2 cm gap; one hinge per door (a Long Door Hinge looks like a run of knuckles, but it is one joint); doors open one way, with no way to open inwards but turning the hinge; F works on the whole leaf and the use area only lights; the Hinge's own leaf takes any panel too.

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
- **Salvage.** With the tool the part needs (the attach tool) out, hold aim on a part: the same timed removal as taking
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

A construct's own interior as storage. The **Storage Volume** (`Constructs/Parts/Storage/StorageVolume.json`, attach tool, 14 kg, 400 health) is a small hopper with a box of space on its construct that you size yourself. It is one custom-sized store for **bulk or parts**: one bulk material, as a bin holds, or stored parts by kind, never both at once.

- **Why a part of its own, not a mode of the Cargo Volume.** A cargo volume is a logic part: its signal freezes bodies and it has no store. Everything a bin does (the store, chutes, F, the look line, spilling, mass) hangs off `PartRole.Storage` and `CPart.Store`, so a storage part with the cargo volume's six box settings gets all of that, and the box comes along with the sizing tool for nothing. One part with two modes would have needed a store on a logic part and a per-part switch at every one of those places.
- **Sizing: the cargo volume's own tool.** With the configure tool out, every box near you shows a handle on each face: hold Fire on one and drag (10 cm grid, the opposite face stays); R on the hopper then Fire-drag draws a new box corner to corner; Fire on the hopper opens the panel, where size and centre are numbers (`sizeX/Y/Z` 0.2 to 40 m, `atX/Y/Z` up to 40 m from the part). While you drag, the look line says what that box would hold. A big box is set from wherever its face is: the server measures your reach to the box, not to the part. Handles only show within the tool's 8 m, so walk to a far face.
- **What counts: the whole box as you size it** (`ConstructStoreVolume.Usable`; the developer, 2026-11-02). A box isn't clipped to the construct, and its fill draws to that size. The box and fill don't collide, so walls are the player's choice, at a price: [Sealed volumes](#sealed-volumes).
- **Set by hand in creative.** The configure tool's `material` and `fill` settings put that much of a material in the box, up to what its open faces leave. They are creative only, as a Fluid Tank's: the server refuses them in survival and the panel shows them read-only. They follow the store four times a second (`ConstructStoreVolume.Follow`), so the panel, a blueprint and a paste say what is in it; a box of parts refuses them ("take them out first"). A survival blueprint or paste arrives empty.
- **Capacity.** Bulk: `binUnitsPerCubicMetre` in `Constructs/Recycling.json` (400 a cubic metre, so 60 m3 is 24,000 units, and iron ore is 1.6 t a cubic metre), at most 10,000,000 units a box. Parts: each takes its bounding box (at least a litre) out of the usable box times `itemPackShare` (0.7: the gaps), at most 256 kinds and 60,000 of a kind. Every part that is an item goes in, however many: physics-only parts are refused as for any storage (a cargo volume carries them), and so is storage itself.
- **Not solid.** The box and its fill collide with nothing: you walk through, bodies pass, and a shot passes through the fill. Only a player's look ray meets the fill (`Raycast(..., look: true)`), so F works at the level wherever you stand, and a stray round can't land on a hopper 30 m away. Reach is to the box. The walls are your panels.
- **Its fill** rises along the part's up from the usable box's floor: the material's colour, or a crate colour for parts, a box with three lumps on top, four draws at any size. Parts are not drawn one by one: a level and the look line's count stand for them ("Storage Volume: 34 parts, 3 kinds, 62% full: Lamp x10, Engine Mount x2... - F: take a Lamp"). Bulk: "Storage Volume: 1200/24000 iron ore - F: take 100".
- **Using it.** F tips in the materials you carry, or takes a load of 100; with parts in it F takes one of the kind put in last into your hands (the same rules as a slot: full hands go to the backpack, or it's refused). Holding a part, a click at the hopper or the fill puts it in, and the look line says why when it won't go.
- **Chutes.** Two named ports on top of the hopper: **Chute in** (green) and **Chute out** (orange), one way each, both in the linking tool's picker. In a blueprint a chute names them with `"toPort": "Chute in"`, `"fromPort": "Chute out"`; a chute that names none takes the only port that way. A chute moves a load of 160 bulk (the Storage Volume's `chuteLoad`, like a Plasma machine) or one part a second (`ConstructRoutes.ChuteMove`): parts come from slots or another volume, and go on into slots or a volume, and what won't go stays at the source. Named chute ports work on any storage part: a part with `"type": "chute"` ports has them instead of its plain one.
- **Mass.** A load counts at the fill's centre through the usual path (`Construct.LoadOf`, `LoadStep` at 4 Hz, "Bin loads"); stored parts weigh their own mass, unlike parts in slots. `LoadKey` is a double, so one unit off a huge load still shows. 10,000,000 units of gold ore is 80,000 t: a buggy's wheels bottom out and it stays put, with every number finite.
- **Limits.** 4 Storage Volumes a construct. A box may not grow into another on its segment ("that box runs into another Storage Volume"; a fresh part's default box may start overlapping one, and only growing the overlap is refused). A box too small for what it holds is refused and undone ("Empty it first").
- **When the construct changes.** The store moves with the part through a split. Capacity follows the bounds, so a split or a lost wall can leave it **over-full**: nothing is lost, the load keeps its weight, it draws full, and nothing more goes in until it is under again. The attach tool refuses to take off a Storage Volume that holds anything ("empty the Storage Volume first"). The remover still deletes it (and spills). A destroyed one spills up to 24 Material Bundles, or loose parts, in the box; the rest is lost and logged.
- **Survival.** It costs what its part costs, whatever the box's size; the box itself is free. Anyone may take from it, as from any store, and the contested rule (decision 19) holds for F, a click and the configure tool.
- **Blueprints.** The box is its settings, so a step's `"config"` holds it and `ConstructConfig.ToJson` writes it; `ConstructRoutes.LinksJson` writes the ports. Bulk contents ride in `material` and `fill`, in creative only; stored parts aren't in blueprints. The **Storage Bay** blueprint is the example.
- **Network (protocol 116).** Two more bits in a store's flags byte: 8, the amount's top u16 follows (a store under 65,536 costs what it did); 16, parts follow (u16 kinds, then u16 part and u16 count: 4 bytes a kind, 1,032 bytes at 256). `CBody` already carries settings and port bytes. Bad values are refused.
- **Performance.** The cost is a box clip per look at the store; a kinds list of at most 256, walked on a change. The arrays are made once, the first time parts go in, and stay with the pooled store. The steady path (chutes, the load step, fill and fit checks) allocates nothing.
- **Reading any store** (for things that list or fetch across stores): `ConstructWorld.Holdings(c, part, span)` writes one `CHolding(IsPart, Id, Count)` per kind held, for slots, bins and volumes, on either side; `ConstructWorld.TakeHolding(c, part, what, count)` takes up to N of it on the server and returns how many came out. Reach and the contested rule are the caller's.

Code: `Source/Constructs/ConstructStoreVolume.cs`, `ConstructCargo.cs` (the box), `Source/App/Program.SandboxCargo.cs` (the handles), `Source/Render/ConstructRenderCargo.cs`. **Self-test**: "Custom bins" and "Custom bins network" (`Source/App/SelfTestCustomBin.cs`).

Choices made (the developer may change them): a separate part, not a cargo volume mode; 400 units a cubic metre against a Materials Bin's 2,200, since loose bulk packs less tightly than a bin's rated load; 40 m and 10,000,000 units; 160 a second a chute however big the box, so filling 24,000 by one chute takes about 2.5 minutes; over-full rather than spilling the excess; the hopper is the one weak point: destroy it and nearly all of a huge load is lost; parts drawn as a level; the kind put in last comes out first by F and by chute; no pick of a kind at the unit itself.

## Salvage haul (decision 31)

Board item dev-sandbox-salvage-haul-sh01, the developer's decision 31 (2026-10-31). It replaces the on-site scrap above and is the
first piece of decision 14's refining chain.

- **No on-site scrap.** There is no scrap count on the player, no "salvage for N scrap", no Scrap Bin. Taking a wrecked
  part off a construct (the attach tool, the same timed edit) leaves a **wrecked part**: a single-part body that is
  darkened, damaged and carries the part's mass and material. It is a body whatever its size (a lamp or an engine); the
  physics gun, a hoist, a cargo volume or a vehicle carry it. It **can't be fitted** to anything (a wrecked part has no
  sockets to build on) and a lone wrecked part can't be salvaged again ("a wrecked part already: haul it to a Recycler").
  Debris that broke off is the same thing. Wrecked parts clear away like other debris. An **intact** part taken off is
  still a usable part (decision 7); the contested-construct rule for enemy wrecks is unchanged.
- **The Recycler** (`Constructs/Parts/Storage/Recycler.json`, attach tool, 220 kg: physics-only like any attach tool part): a steel
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
- **Saw Module and Drill Module** (`Constructs/Parts/Gathering`, attach tool, 900 W at tier 1, five tiers: [Machine tiers](#machine-tiers);
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
right after a split, an attachment, a spill or a part taken off. A deposit or a withdrawal, the two places a server's bulk changes,
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

## Scoops

A scoop is a bucket for an arm: a bin with a cutting edge. Push the edge into the ground and it digs out a chunk, and what it cut lands in the bucket as bulk material. Tip it and that runs out over its lowest rim, into a store underneath if there is one that takes it, else onto the world. Parts in `Constructs/Parts/Scoops`, written by `tools/constructs/scoop_parts.py`; code in `Source/Constructs/ConstructScoops.cs`, the game's side in `Source/Game/GameSandboxScoops.cs`.

**The styles.** Four, in the four size classes (16 parts; the inventory has them under Production, Scoops):

| Style | Small width | Holds (small) | What it does |
| --- | --- | --- | --- |
| Digging Bucket | 1.25 m | 7 | The all-rounder: teeth, two round bites side by side. Slowed to 0.4 in stone. |
| Grading Bucket | 1.75 m | 7 | Wide and shallow, a plain lip: three flat bites across, each a `DigKind.Crater`. Holds the same on a lighter cut, poor in stone (0.12). Tips soonest. |
| Trenching Bucket | 0.75 m | 4 | Narrow, deep and strong: one round bite the width of its edge. 0.6 in stone. |
| Rock Bucket | 1.25 m | 6 | Bars and big teeth: full speed in stone, and feels its hardness least. Dirt and sand mostly fall through the bars (`keep`: 0.3 and 0.1). |

Medium is 3 m (the Trenching 1 m, the Grading 5 m), Large 12 m, Capital 48 m. Capacity is the fill box's volume at 10 units a cubic metre (`Constructs/Digging.json`), so what a bucket carries is what it dug and it fills back the same ground. Mass goes with the plate. A scoop goes on by its `back`, `top` or `under` socket.

**The data.** A scoop is a storage part with `bulk` (material `any`) and a `scoop` block, in the part's frame: `edge` (two points), `rim` (two points: the far side of the mouth), `cuts` (the way the edge points), `mouth` (the open side), `reach` (how deep the edge counts), `cutForce` (N, on dirt), `biteRadius`, `bites`, `flat`, `spillDegrees`, `pourSeconds`, `stoneRate`, `keep`. The interior is the bulk fill box. Being a bin gives it the rest: F takes a load or tips in what you carry, chutes, mass, the drawn fill rising with the load, the look line, spilling when it breaks, the wire, saves. Blueprints carry the part, empty, like any bin. Beacons put nothing in one. The ground's numbers are the `scoops` block of `Constructs/Digging.json`.

**Digging** (each tick, server, awake bodies only). The edge's real motion is read from where it was a tick ago, so a hinge, a piston, a rail and a whole vehicle are the same thing. A parked scoop moves nothing and digs nothing.

- *The ground gives way.* While the edge is in the ground (`Engaged`) the scoop's boxes stay out of the ground's contacts (`ConstructPhysicsContacts`, `ConstructCollider.Leaf.Scoop`) and one row at the edge stands for them (`ScoopRows`): a contact whose impulse is capped at the ground's strength (`Contact.Cap`), with a friction limit of its own (`Contact.Drag`). Under `cutForce` it stands on the ground; past it, it moves. So a parked bucket rests, a strong arm goes in and a weak one stalls. `cutForce` is never under 2.5 times the bucket's weight.
- *Bites.* Moving at least 3 cm/s with a fifth of that along the way the edge points, it sweeps edge width x depth x speed. Swept ground adds up to a bite: one to three balls just ahead of the edge, taken out by the usual `Game.Dig`. Each is measured first (`TerrainEdits.Measure`), so what lands in the bucket is what left the ground, down to fractions kept in `CStore.JobAmount`. Pushed faster than it bites (at most one bite in 0.25 s, four in a tenth of a second across the world), the ground holds it harder.
- *What it gets.* The ground's own material, through the table: dirt, stone under 3 m or on rock, sand, or what a fill laid. A scoop holds one material, the most of its first bite. Later bites give only that; a bite that is less than half that stops it ("this ground isn't dirt: tip it out first"), and the ground holds it as if it were full. A scoop given ore by hand or chute doesn't dig.
- *Full.* It stops digging. The ground holds it at `fullForce` (8) times its cut force against going deeper, and lets it go coming out. An edge that isn't leading is the same. A scoop that isn't in the ground meets it with its boxes, like any part.
- *Wedged.* Ground goes in half-metre columns, and a bucket can lodge on one between the points it feels at. A joint it hangs on pushing past 1.5 times its rating within 30 cm of the ground counts as being in it, so an arm can always bring a full bucket out. Bedrock and the map's edge can't be cut: it meets those as a solid.
- *Limits.* Scoops stop at 75% of the terrain log (`scoops.logShare`). A loose scoop, a broken one, and any scoop where ground can't be dug (`Game.TerrainDigLive` false: Destruction off, no terrain) is solid and doesn't dig. A planetoid digs through the same calls; that hasn't been run.

**Tipping** (10 Hz, sleeping bodies too while there is a load). Where gravity's up stands against `mouth`: past `spillDegrees` the contents leave at up to capacity / `pourSeconds`, a fifth of that just past the angle, all of it about 20 degrees past. With no gravity nothing falls out, and with its lip in the ground nothing can.

A ray goes straight down from the middle of the lowest side of the mouth, up to 80 m:

- A store it hits first takes it: a bin, a machine's input (`Feed`), an Assembler's, or a Storage Volume, whose box counts though it isn't solid, as long as the ray reaches the box first. A truck bed is a Storage Volume sized to the bed, or a bin on the deck. Its own construct counts, so a machine can load itself. What a full or unsuitable store won't take carries on as below.
- Open ground takes **dirt, stone and sand as fill** (`DigKind.FillDirt`...), a lump at a time (an eighth of the bucket, at least 4 units), on the lowest spot just round where it lands, so a pour spreads into a heap. It is ground like any other: scoops and the ground drill dig it back for what it was. The drill's rules apply (nobody buried, 16 m over the surface). A fill never holds more than went in; the last few units of a load, too few to fill anything, are lost.
- Anything else: **Material Bundles** fall from the lip with the scoop's velocity. That is on a construct that won't take it, a building, ground that refused the fill, ore, or Destruction off. A bundle is worth what went in (`CPart.Rounds`; the Recycler gives that back; a broken bin's bundles too now, which were worth 100 however little was in them). They are debris: they clear away, and only a Recycler takes them back. A Cargo Volume carries them once they are in it, as any loose body.

**The look line.** `Digging Bucket: 12/40 dirt, digging - full: tip it out - F: take 12 dirt`. `Busy` says digging, and `CStore.Blocked` says why it stopped or where a pour is going, both on the wire with the store. Nothing new on the wire, so the protocol stands.

**Cost.** 96 scoops awake near the ground: about 0.34 ms a tick in the container, nothing allocated. A sleeping one costs a test, or a look at its angle at 10 Hz while it holds a load. Every scoop takes a store handle, and a world has 128, like every store.

Choices made (the developer may change them): 10 units a cubic metre, the sculpting rate, so scoops and the drill balance, which makes a cubic metre of dirt 15 kg (`kgPerUnit` 1.5), so a load barely weighs on an arm; one material a scoop; the part of a bite that isn't what it holds is lost, as is what a skeleton bucket drops; fill for ground kinds, bundles for the rest; a pour lands at once, with nothing drawn falling; the fill draws as a box rising from the bucket's floor and doesn't tilt with it; no clamshell, since one needs moving jaws and has to hold its load upside down; a scoop needs no power.

## Refining (decision 14)

The step after gathering: raw material goes into production machines, on a base or a big vehicle, and refined material comes
out. Chutes move it between them. [Assembling](#assembling) uses the results. The chain has three steps: raw ore and wood; first tier
materials (a Smelter's metal, copper, gold and glass, a Sawmill's planks and cloth, with stone and sand as they come out of the ground); second tier materials
from a **Foundry**, which mixes two first tier ones.

- **The machines** (`Constructs/Parts/Refining`, attach tool, physics-only; five tiers each, the numbers here are tier 1's, and what each looks like is tier 2's, the plainest: each tier has its own shape, [Machine tiers](#machine-tiers)). All are buildings. The **Smelter** (3.5 m, 5,145 kg, 3 kW): a brick furnace with
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
**Extruder** pushes bulk material through a die; what comes out is a real, parametric piece; a signal into its **Cut** input, or
its Cut every length, parts it off at the die. Code: `Source/Constructs/ConstructExtrusion.cs` (the pieces), `ConstructExtruder.cs` (the machines),
`ConstructExtrusionTable.cs` (`Constructs/Extrusion.json`). The parts and the Extruder Line blueprint come from `tools/constructs/extrusion_parts.py`.
Self-test: "Extruder", "Extruder network" (`Source/App/SelfTestExtruder.cs`).

- **A piece** is a cross-section swept along a length: an 8 by 8 cell grid (a u64: bit y * 8 + x), a width and height (0.1 to 32 m),
  a length (0.05 to 192 m, in 5 cm steps, so both the 0.1 m and 0.25 m lattices can be hit) and a material. That is all it is
  (`ExtrusionKey`). The grid is meshed into at most 16 solid boxes, the same way on every machine; a solid bar is one box, an
  I-beam three. Presets: Solid bar, Plate, Wall (a skin each side on three studs), Hollow box, I-beam, C-channel, L-angle, T-bar, Cross;
  Custom is the panel's own grid. A section must be connected and mesh into 16 boxes; each refusal says why.
- **Round and Pipe** (the Profile's last two choices). The key's `Shape` is `Round` or `Pipe`, with no grid: Width is the diameter (`W` and `H` both hold it,
  and the die must take it both ways) and a Pipe's **Pipe wall** setting is `Wall`, in cm, 1 to 200, always leaving a 5 cm bore. Mass, health and material use follow the real
  area. **Collision.** A finished Round is a convex hull of its drawn fourteen-sided prism over three hidden boxes; while it grows it is the boxes, since a hull is made once and
  a growing piece is a new def four times a second. A Pipe is 32 hidden boxes round the bore, `ColliderBoxes` set so the tube's outer hull is not made: the bore is open to rays,
  bodies and players, who can walk inside a big one. Boxes don't turn, so the ring is stepped: true on the axes, about 9 % of the radius proud of the circle and into the bore on the diagonals. **Sockets.**
  `base` and `tip` in the middle of each end (a pipe's stand in the bore's air: a weld to them is a weld to the end), and the four faces at the same stations, at the compass points of the round.
  **For later machines.** The key holds all a die needs to read a piece: `Shape`, `W`, `H`, `Wall`, `Mask`, `L`, `Material`.
- **A piece is a real part.** `Extrusion.Def(key)` makes a `ConstructPartDef` the first time a key is asked for and keeps it: ids from
  `Extrusion.RuntimeBase` (0x4000), resolved by `ConstructParts.Def`, never sent. So `Rebuild`, split, join, the renderer, rays, player
  blocking, ghosts, joins, damage and paint read a piece's boxes, sockets, mass and health as they read any part's, with no second path.
  `ConstructParts.All` doesn't list them, so menus, racks and part sweeps see only the files. Bounds are the real section's; collision
  and hits are its solid boxes through `Construct.SolidCount/SolidBox`.
- **Numbers.** `Constructs/Extrusion.json` by material: `unitsPerM3` (mass is that at the material's `kgPerUnit`, so a piece weighs what
  went in), `healthPerM3`, `armourPerCm` of its thinnest wall up to `maxArmour`, and `class` (salvage, hits, burning). A row may name a
  material Recycling.json doesn't have yet (steel, concrete): it is simply not usable until it does. Metal: 200 units/m3, 400 kg/m3.
- **Sockets.** A finished piece has `base` and `tip`, and four faces (`+x0`, `-x0`, `+y0`, `-y0`...) at up to nine stations about 1 m
  apart (further on a long piece), 38 at most, each on the outermost cell of that side nearest the middle. Point and freeform attachments reach everywhere else. A
  piece is physics-only ("extruded"): attach tool, physics gun, cargo volumes.
- **The Extruder** (`Constructs/Parts/Extrusion/Extruder.json`, `storage.extrude`): a bin (`any`, 400) with a die at its +z face, 0.2 m/s, 900 W
  while it runs. Material comes by chute or F; Run and power as a [production machine](#refining) (`ConstructGatherer.HeldOff`,
  `ConstructRecycler.Powered`; creative needs no power but still needs material). Its output wire is the length so far, in metres.
  Configure tool: Profile, Width, Height, Pipe wall, Cut every, and the cell grid (four `mask` settings drawn as one grid: click a cell and what is shown
  becomes Custom; a Round or a Pipe shows on it too). A change of setting is for the next piece; the panel previews the section on the die.
- **By size class.** The limits are part data.

  | Machine | Die | Longest piece | Speed | Bin | Power |
  | --- | --- | --- | --- | --- | --- |
  | Extruder (Small) | 0.5 m, Die Frames to 6 x 6 m | 48 m | 0.2 m/s | 400 | 900 W |
  | Medium Extruder | 8 x 8 m | 64 m | 0.4 m/s | 2,000 | 3.6 kW |
  | Large Extruder | 16 x 16 m | 128 m | 0.8 m/s | 8,000 | 14.4 kW |
  | Capital Extruder | 32 x 32 m | 192 m | 1.6 m/s | 32,000 | 57.6 kW |

  The big machines are their die (a block of their class's cells, `sizeClass` sockets) and take no frames. 192 m keeps a piece inside `ConstructParts.MaxPartReach` (200 m). A piece's mass and health stop at `MaxPartMass`
  and `MaxPartHealth` (50,000 t, 16,000,000): past those a huge solid piece still costs what went in, but weighs and takes hits as the limit does. The table holds 48,000 pieces.
- **Die Frames** (`"dieFrame": true`; Die Frame 0.5 x 1 m, Large Die Frame 1 x 2 m). The small Extruder starts with a 0.5 by 0.5 m die. Attach frames to it (and
  to each other) on any side: the die reaches across every working frame joined to it, up to 6 by 6 m, and its centre moves with
  them (`ConstructExtruder.Die`). The section is held to the die, and the look line says to add frames.
- **Growing.** The piece is a part of the machine's own construct, linked to its `die` socket (kind `die`: nothing else fits), its origin
  at its base end, so it rides with the machine (a mothership included), counts in its mass and is solid. Everything is found from that link
  (`ProductOf`), so nothing has to be kept in step through renumbering, splits or the wire. At 10 Hz the step adds up
  growth; each 5 cm swaps the part's def for the longer one and calls `Construct.RefreshShape` (mass, bounds, contact points; no routes, power, drive line or
  guns), and sends a 5-byte `CBodyKind.Extrude`. At 0.2 m/s that is 4 swaps a second. A shape is made once, so a line in steady work allocates only the bodies it cuts off.
  A growing piece has one socket (the die's), so nothing can be fitted to it, and joins onto it are refused. A piece no longer linked
  to a die (taken off, the machine gone) finishes at the next rebuild, with its full sockets.
- **Its path.** Each step checks the new slab, box by box (an I-beam's notch stays a notch): the ground, walls, its own construct and anything
  anchored stop it ("blocked"). A free body is pushed along the path (`pushForce`, 6 kN, at most 0.6 m/s) and growth waits for it to go, so
  nothing is ever grown into; one that doesn't move in 1.5 s stops it ("too heavy to push").
- **Why it stopped** is the store's `Refused` byte (`ExtrudeStall`, on the wire): no power, needs material, can't extrude that, the bin holds another
  material than the piece, the section can't be made, at the cap, blocked, too heavy. The look line has the bin, the die, the section, the length so far, the
  material use and the reason.
- **The cut.** The machine's second input, **Cut**: a rising signal (caught at the logic's rate, so a one-tick pulse counts) parts the whole piece off at the die at its next step. It needs power; it works with Run held off.
  **Cut every** (`cutLength`, metres, 0: off) does the same with no logic when the piece reaches that length, and growth stops there, so lengths are exact at any speed. The piece comes off as a body of its own where it stands,
  moving as the machine moved, keeping its health share and paint, and the part leaves the machine's construct (a full rebuild of that construct, once a cut). The next piece starts from the settings as they stand. Until it has fallen or been taken away a cut piece is
  in the next one's path: it is pushed along as any free body is. Without a cut a piece grows to the cap and stops.
- **On the wire (protocol 143).** A piece in a Full's part list is a mark (0xFFFE) and its key, 10 bytes with a preset section, 18 with a custom one, 10 with a round or a wheel and 12 with a pipe; ids
  are each machine's own, so `StructureHash` mixes the key's hash. `CBodyKind.Extrude` carries width, height and length. A client out of table room (`Extrusion.MaxDefs`, 48,000 pieces in a run) shows a stand-in, and a server
  there stalls with "too many different pieces".
- **Blueprints and saves.** A piece names itself: `"part": "Extrusion i-beam 25x25x400 metal"` (or 16 hex digits for a custom section), `Extrusion round 50x50x400 metal`, `Extrusion pipe3 50x50x400 metal` (a 3 cm wall) or `Extrusion wheel 30x100x0 rubber`
  (30 cm wide, 1 m across) in any step, map list or world save. `base`, `tip`, the face names and a wheel's `axle` are its sockets. **Extruder Line**: a Foundation, an Extruder, a Battery, a Materials Bin chuted into the hopper, and a Timer wired to the Extruder's Cut.
- **The Cut-off Saws are gone** (2026-11-04). A saved world holding one loads without it: the world's log says "left out N x Cut-off Saw", and what only the saw joined to its construct splits off. A blueprint naming one is refused at that step with the part's name, as any unknown part. The saw's job is the Cut input and Cut every.

Choices made (the developer may change them): the cut is at the die and takes the whole piece (a saw could stand anywhere along the path, and left the rest on the die); Cut every moved from the saw to the Extruder rather than going, so a line needs no logic; a def per length rather than per-part data (the alternative needs every reader of boxes and sockets changed, and the
table is cheap: one line is at most 640 lengths of a section); an 8 by 8 grid (a u64, a simple panel; finer needs a second word); 5 cm steps; the piece on the machine's
own construct, not a body of its own (so it uses one of the construct's 8,000 parts, and growing on a free vehicle gives no reaction); a stalled piece keeps its length; the table of limits by class;
one Die Frame turned to suit rather than a wide and a tall one; a Die Frame counts in any turn; until feat/collision merges, a moving piece touches the map and fixed bodies only with its bounds corners, as every body does (something moving into a fixed piece does meet its real boxes: a post stops at an I-beam's web; the compound collider gives the moving piece the same); tiers aren't built, but `speed`, `die`, `maxSection`, `maxLength` and `pushForce` are part data.

## The Wheel Former

An [Extruder](#extrusion) with a hub for a die: it builds a wheel up on the hub, in place, fed with material the same way, and a Cut signal parts the finished wheel off. Code: `ConstructExtruder.GrowWheel`, `ConstructExtrusion.BuildWheel`; the numbers are the `wheels` rows of `Constructs/Extrusion.json`; the parts come from `tools/constructs/extrusion_parts.py` (`storage.wheel`). Self-test: the Extruder sections.

- **Run modes.** **Width** builds along the axle, **Depth** builds the diameter (depth reads as how far the wheel stands out from its hub: both radii together, so a mode's step adds 5 cm to the diameter). The **Run mode** setting picks one; a wire into the **Mode** input decides instead (on: Depth). Each may be changed at any time; the wheel keeps what it has. The inputs are **Run**, **Cut** and **Mode**. The output wire is the wheel's size in the running mode, in metres, so a Compare wired from it to Run or Cut stops a wheel at a size. A wheel at a limit says so on the look line and waits. The first wheel is paid for in one go, so it fits in a bin.
- **Limits.**

  | Machine | Widest | Biggest across | First wheel | Speed | Bin | Power |
  | --- | --- | --- | --- | --- | --- | --- |
  | Wheel Former (Small) | 1.5 m | 3 m | 0.1 x 0.4 m | 0.1 m/s | 400 | 900 W |
  | Medium Wheel Former | 6 m | 12 m | 0.5 x 2 m | 0.2 m/s | 2,000 | 3.6 kW |
  | Large Wheel Former | 12 m | 24 m | 1 x 4 m | 0.4 m/s | 8,000 | 14.4 kW |
  | Capital Wheel Former | 20 m | 48 m | 2 x 8 m | 0.8 m/s | 32,000 | 57.6 kW |

  Each hub stands high enough for its biggest wheel over the machine's own floor. A wheel that meets the ground, something fixed or its own construct stops with "blocked"; a free body is pushed out from the hub.
- **What it makes** is a run-time part as an extrusion is: key `Shape = Wheel`, `W` its width, `H` its diameter, `L` 0, in 5 cm steps. It is one def per size, at any material in `wheels`: rubber (grip 1.0, flat when broken, like a road tyre), metal, steel, wood, planks, stone, concrete. Mass is the cylinder's volume at the row's `unitsPerM3` (mostly air and rim: a rubber wheel 0.3 x 1 m is 39 kg), and the cost is what went in. A wheel's origin is on its axle face, so width grows away from the hub's plate and only the def changes. Off the machine it has an `axle` socket (the size class's by diameter: under 2 m small, under 7 m medium, under 24 m large), and all a wheel's numbers: `CWheel` reads its radius and width from the def and bounds, its cylinder collider follows, and it has the Tyre settings of every wheel. Loose, it collides as three hidden boxes (as every wheel does). It is physics-only: the socket weld puts it on a suspension's axle.

Choices made (the developer may change them): depth as the diameter; the `wheels` rows; the limits above; the machines' size; a wheel as one solid material (no rim and tyre of two); no Interact to cut (a Button wired to Cut does it).

## The Press

The user's "complex parts made in the world, not in menus". A **Press** stamps parts out of extruded pieces; it sits beside the Extruder and the Wheel Former, and the [Assembler](#assembling) stays for everything no die covers yet. Code: `Source/Constructs/ConstructPress.cs`, `ConstructPressTable.cs` (`Constructs/Pressing.json`); the parts, the table and the Press Line blueprint come from `tools/constructs/press_parts.py`.

- **Modular by size.** Press, Die Lathe and die each come in the four size classes, and a die fits a press of its own class (socket kind `pressdie`, the class of the socket). The Small press is 1.75 m wide with 2 bays and takes pieces to 0.45 m across; Medium, Large and Capital have 3 bays and take 1.8, 7.2 and 28.8 m, at speed x2, x4 and x8. Length sets count, so the player builds the smallest system for the job.

  | Machine | Bays | Across | Stroke speed | Power |
  | --- | --- | --- | --- | --- |
  | Press | 2 | 0.45 m | x1 | 900 W |
  | Medium Press | 3 | 1.8 m | x2 | 3.6 kW |
  | Large Press | 3 | 7.2 m | x4 | 14.4 kW |
  | Capital Press | 3 | 28.8 m | x8 | 57.6 kW |

- **What is in it is a part.** A bay and the die slot are sockets, and a piece or a die in them is a part of the machine's own construct, linked to that socket, as a piece on an Extruder's die is. So it counts in the mass, is solid and drawn, and goes through the wire, world saves and blueprints with no second path; `ConstructPress.PieceIn` and `DieIn` find it from the link. A piece in a bay keeps its `Growing` key (no sockets; nothing welds to it) and stands out of the bay's mouth. It has to fit where it would stand.
- **Fed by hand.** Let a loose piece go with an end in a bay's mouth (`bays[].min/max`, checked at 10 Hz) and it is drawn in. A die: F with it in your hands, in the same way the attach tool would fit it to the slot (the die in there comes back to your hands), or a physics-only die (Large, Capital) let go in the front of the machine. F empty-handed takes out what is nearest: a piece slides out of its mouth, and isn't taken in again for 3 s. Look line: `Press: Button Die - bay 1: wrong material, wants laminate plate - F: take the die out`.
- **Stock always comes out.** What a press, a Die Lathe, an Extruder or a Wheel Former holds comes out in every state: too short, the wrong die or none, no power, held off, a stroke under way. Aim at it (the crosshair lands on the piece, not the machine: `ConstructPress.MachineOf`) and press F, or take it with the physics gun, which draws it out and holds it; on an anchored machine too. F on the machine takes the nearest. It comes back whole, at its length with its health and paint; a stroke takes length only when it ends, so one under way just stops and uses nothing. A piece shorter than a stroke is refused at the mouth ("too short") and stays out; the stub a run leaves stays in its bay until taken. A piece taken out isn't drawn back in until it has left the mouth or been picked up (`Construct.TakenFrom`).
- **Fed by chute.** A chute from an Extruder to a press takes each finished piece whole: at Cut, or at Cut every, the piece waits on the die (`ExtrudeStall.ChuteWaits`) until a chute move finds a bay that will have it, then moves. A chute from a shelf or a Die Rack brings a die of the press's size to an empty slot. A chute of bulk is refused. The press doesn't name its ports: it picks the first free bay that takes the piece. What a stroke makes goes out of the tray by chute as any slots do.
- **Pressing.json.** `rows` is flat: one row per die, material and size band. A row names `die`, `output`, `count`, `seconds` and its `bays`, each with `label`, `sections` (Extrusion presets, `round`, `pipe`), `materials`, `minAcross` and `maxAcross` (the piece's smaller side), and what a stroke takes: `use` (m), for parts of a fixed size, or `useAcross` (times its size across). The first row whose bays are all satisfied runs. Rows of a die stand together; a die has no list of its own, so 15 dies times materials times sizes is only more rows. `lathe` rows are chosen as a setting.
- **What the die, the material and the piece do.** The die sets the family; choosing a recipe is swapping it. One die covers every material of its family. Fixed-size parts take their count from length: at `use` 0.1 a 2 m strip is 20 strokes, and a piece with less left than a stroke stops with "too short". Parts with no fixed size follow the section: the rows' bands pick the output, so a 0.15 m bar in a Housing Die makes Housing 0.25 m and a 0.3 m bar Housing 0.5 m.
- **Stroke.** A row's `seconds` at the machine's speed. Power, Run wire and creative as a [production machine](#refining); a body that can't leave waits with the pieces untouched. Output in the tray, 12 a slot, or as a body at the front (physics-only, or too big: brackets, housings, dies).
- **The first set** (33 rows):

  | Die | Bays | Makes | By |
  | --- | --- | --- | --- |
  | Button | laminate plate + copper round | Button | length: 10 cm + 5 cm a Button |
  | Board | laminate plate (20 cm up) + copper round | Circuit Board | length |
  | Bracket | metal, steel or bronze L-angle or plate | L Bracket, Corner Bracket, Gusset 0.25 m, Gusset 0.5 m | section |
  | Gear | steel round | Gear Set | length |
  | Piston | steel round + bronze bar | Piston Set | length |
  | Housing (4 sizes) | metal, steel or bronze hollow box or bar | Housing 0.25 to 16 m, 7 sizes, a set in each material: 21 new parts | section, material |
  | Cylinder (4 sizes) | steel pipe + steel plate | Small to Capital Cylinder | section |

  The rest of the Assembler's list stays with the Assembler. **Substitutions:** the game has no plastic or aluminium, so plastic is laminate and aluminium is bronze; a strip, sheet or copper wire is a plate or a 10 cm round (`Extrusion.MinSectionCm`). Laminate and bronze got rows in `Extrusion.json`.
- **Tiers.** Material sets the tier only where the game has a part per tier. Housings do, since they are new. Bracket, Gear Set, Piston Set, Button, Circuit Board and Cylinder have one part each, so those rows take the materials that suit and give that part.
- **Dies.** Parts in `Constructs/Parts/Press/Dies`, `"pressDie": "Button"`, first socket the mount. Small and Medium are items (on a Die Rack or a shelf, in hands); Large and Capital are physics-only. Starter set, free with a survival start (`SurvivalStart.json`): Button, Bracket, Gear. All the rest come from a Die Lathe, not the Assembler.
- **The Die Lathe** (`"press": {"lathe": true}`) is the press's code with one bay and no slot: a setting chooses the die (`"config": {"recipe": "Board Die"}`). It takes a solid bar: metal or steel for a starter, steel otherwise, at least the die's own size across (0.25, 1, 4, 8 m), and a stroke uses that length, so a 1 m block cuts four Small dies. A lathe cuts dies of its own size class ("Wrong size of lathe").
- **Refusals.** A piece is turned away at the bay and stays where it is. The panel and the look line say why (`BayRefuse`: no die, wrong material, section, size, no room for its length, wrong die size, bulk). It rides in `CStore.Blocked` (reason x 4 + bay, 3 for the die slot) and clears after 5 s or when a die goes in.
- **Performance.** `ConstructPress.Step` at 10 Hz looks only at constructs with `HasPress`; a bay's mouth is a `Near` query only while the bay is empty. Nothing allocates but the bodies it makes.
- **Wire (protocol 144).** No new message. Parts and links carry what is in it, `CBodyKind.Extrude` a shorter piece; the stall is `Refused`, the refusal `Blocked`.

Choices made (the developer may change them): one part a press, not a frame you add bays to; a Small press has 2 bays and no recipe uses a third yet; a die only in a press of its own class, never a bigger one; a piece's size is its smaller side; a stroke's cost is a length, not a mass, so a stamped part doesn't weigh what it used; the Housing family is new, with no use yet beyond a box with sockets, and the Assembler can build them too; chutes carry pieces only from an Extruder (a shelf can't hold a piece, since a piece is physics-only); a press's ram doesn't move, as no part animates its work yet.

## Machine screens

Production machines have a screen on their own model, source `machine` (`Source/Constructs/MachineScreens.cs`): a dark plate that `tools/constructs/screens.py` places on each model, on the flattest free spot near reading height, clear of sockets, ports and dies, since tiers and sizes differ. Every tier of the Smelter, Sawmill, Foundry and Assembler; every size of the Extruder and Wheel Former; the Recycler (`Constructs/Parts/Storage/Recycler.json`, by hand). The Press and Die Lathe have their own bay screens; gathering modules have none: they work on arms out of sight, and the look line says enough.

| Line | Refiner | Assembler | Extruder, Wheel Former | Recycler |
| --- | --- | --- | --- | --- |
| Top, in the state's colour | Refining, Needs input, Needs more, Needs tier N, Wrong input, Empty output, Output full, No power, Held off | Building, No recipe, Needs input, Needs tier N, Tray full, Output blocked, World is full, No power, Held off | Extruding, Forming, Waiting, Needs material, Wrong material, Other material, Bad section, At full size, Blocked, Path too heavy, Chute waits, No power, Held off | Recycling, Waiting, Output full, No power, Held off |
| Second | what it makes, or a batch | the recipe | the section, or the mode | what it is doing |
| Third | input | what it lacks | stock | output |
| Fourth | output | tray | the piece's length or size | |
| Bar | output fullness (a batch is about a second) | the job | the piece's length, else stock | the job |

Everything comes from the store and the part, so a client shows what the host does. A job's progress is new on the wire (protocol 155): `CStore.Progress`, sent about once a second while it runs and at once when it stops (`MachineScreens.SetProgress`), and only when above 0. Range 30 m, filled at 4 Hz in view, laid out again only when the card changes; a fill allocates nothing. `ink` colours them too.

## Display panels

Flat lit surfaces on a part, and a small card API for what they show (the user: "these UI screens will be utilized in a lot of different parts, so make it modular and make it its own thing"). Code: `Source/Constructs/DisplayPanel.cs` (the data and the card), `Source/Render/PanelRender.cs` (the drawing).

- **In a part file.** `"panels": [{ "source": "press.bay", "slot": 0, "at": [x, y, z], "size": [w, h], "face": "-z", "up": "+y", "rows": 5, "range": 25 }]`, up to 8. `at` stands a few mm off the face behind it (no coplanar faces). `rows` lays the text out, 1 to 6; `range` defaults to 400 x a row's height, 12 to 120 m. `source` names a `PanelSources` entry: a function that fills a card, so a new use is a line there. `status` is any part's name and health.
- **The card** (`PanelCard`): up to 6 lines of 32 characters, the first a title in the state's colour; `State` (off, idle, working, ok, wants, refused, fault), which colours the frame so a row of machines reads across a room; a colour swatch with an 8 by 8 picture on it (a material and a section); a bar. It knows nothing of parts, and filling one allocates nothing: text goes in with `Add` from existing strings and numbers.
- **Where its content comes from.** The source reads the part as it is on this peer, so a client's panels need nothing on the wire: they read the same parts and stores the look line does.
- **How it is drawn.** ImGui can't draw in the world pass (it runs over the finished picture, and only between NewFrame and Render), so a panel is plain world geometry: a shared glyph quad path with a signed distance field font (`PanelFontData.cs`, DejaVu Sans Mono Bold, written by `tools/render/make_panel_font.py`; licence in NOTICE.txt): a mipmapped R8 atlas on texture unit 8, sharp at any size, one texture read a pixel, works on GL 4.1; every cell has a clear margin, so a big glyph never picks up its neighbour, one small shader, unlit, depth-tested, every visible panel in one draw at the end of the opaque pass. There is no render target per panel.
- **What it costs.** `Construct.PanelParts` lists a body's parts with panels, so only those are visited, on bodies the frame draws part by part. A panel past its range, facing away or out of view costs a few comparisons and is never filled. One that shows is filled 4 times a second, and its quads are made again only when the card's hash changes. A frame then only turns quads into a stream buffer, so panels follow moving and jointed parts exactly. 120 frames of a full press allocated 0 bytes in the panel path. At most 128 panels show at once.
- **Off a part.** `PanelRender.DrawCard` puts a card on any surface between `Begin` and `Flush`: a held tool's screen does (Tools in hand). A Smart Terminal's screen is the `terminal` source: `PanelCard.AddRow` sets a list's row out with its count at the right edge, and `Mark` lights a line. A card fills 4 times a second, so what a player's own key changes bumps `PanelRender.Touch`, and panels in view fill again in that frame.

## Drones

The user: "a wireless seat connection. I want the player to be able to control drones." A seated player flies or drives a construct with no seat on it. Code: `Source/Constructs/ConstructRemote.cs`, `Source/Game/GameSandboxRemote.cs`; parts and the Scout Drone and Control Post blueprints: `tools/constructs/drone_parts.py`.

- **The receiver is the seat a drone doesn't have.** A **Drone Receiver** has the seat role with nobody in it (`"remote": {"receiver": true}`): link it into a Driving or Turret Module as any seat, and `DriveSeat`, the module's outputs, guns and the drive line read it as they read a seat. F passes over it. So there is no second control path.
- **The sender.** A **Remote Console** is a seat with a transmitter and a screen, 800 m. A **Remote Transmitter** (800 m) or **Medium Remote Transmitter** (3 km), wired from any seat, lets that seat send. Range is the sender's (`"remote": {"range": 800}`), and needs power where the construct has a circuit. The **channel** is a setting, 1 to 99, the same key as the logic parts' Wireless pair.
- **Server authority.** Input goes up the seat path as ever: the server copies the sitter's keys to the receiver every tick (`Game.SandboxRemote`), runs `DriveInput` on the drone and maps the look from the seat's frame to the receiver's, so a Turret Module aims with the mouse. It looks for the nearest receiver at 10 Hz: on the channel, theirs or nobody's, and not driven by someone else. A client knows its drone from who drives what (`ConstructRemote.Piloted`).
- **Lost signal.** Out of range, or with the receiver or transmitter without power, the drone keeps its pilot and gets no keys: its throttle and steering go to nothing and a flyer's hover hold stays on, so it holds where it is and answers again when the signal is back. It carries on what speed it had until drag stops it. When the pilot gets up it has no driver, as when anyone leaves a seat; the hover hold stays on for a flyer.
- **The pilot's view.** Seated with a drone on the link, the view is full screen from the drone's first camera (`Program.PilotCamera`), or from behind it with none. It is the one main view, so it costs no extra pass. The mouse looks within the seat's limits, turned onto the drone. The third-person key gives you back your own seat. The HUD has its speed, the signal in percent, and the keys.
- **A far drone.** The world is whole on every client. What falls off with distance is how often a body's pose goes out, so a pilot's poses are centred on the drone (`SandboxInterestAt`), and what is round it stays current. A piloted body goes in every pose packet already, as a driven one does.
- **Scout Drone**: a Drone Frame, four Drone Rotors for lift, two Drone Pushers at the back for W and A/D, a Drone Battery, a receiver, a Driving Module and a camera, 34 kg. Sprint lifts it into the hover hold. Light rotors matter: the hover hold is weak off a planet against a big fan on a small body, and a 750 kg version on Light Lift Fans tumbled under its own pushers.

## Cameras and screens

The user: "they have an incredibly short render distance for the player. So if there's no players near a functioning screen in the camera system, the camera just doesn't do anything." Code: `Source/Render/CameraFeeds.cs`, `Source/App/Program.CameraFeeds.cs`, `ConstructRemote.FillScreen`.

- **Camera** (and **Medium Camera**): a small part with a lens (`"camera": {"at": [...], "fov": 70}`), looking along its -Z, on any construct. It moves with its segment, so a gimbal is a joint under it. Its channel is a setting.
- **Camera Screen** (0.5 m, 1 m, 4 m): a display panel with the source `camera`, the nearest camera on its channel. It needs power. Its `range` is 8 to 25 m. The Remote Console has one. Any part file can have one.
- **Nothing happens unless someone is watching.** A panel is only filled when it is in range, faces the player and is in view, on that machine ([Display panels](#display-panels)). Filling a camera panel is what asks for the picture (`CameraFeeds.Request`); a camera unasked for 0.6 s stops and gives its layer back. So a camera with no watched screen has no cost beyond one entry in a list, and nothing goes on the wire: each peer draws from its own copy of the world.
- **The limits.** 4 live cameras, in one 256 by 144 texture array. One pass a frame, the camera longest due, 10 times a second at most, and a pass that took long waits 12 times its own time, up to a second.
- **The pass** (`Program.DrawCameraFeed`) runs before the frame's own, into a render target: the map, its sky, baked light and constructs, on the 4.1 and 4.6 paths. It leaves out foliage, decals, glass, panels and weather, and draws players as plain boxes, because their animation is per frame. There are no shadow maps in the game to redo. It allocates nothing.
- **Measured** on the container's software GL at 1280 x 720, in CPU time to submit: 2 to 4 ms a pass on Construct Yard, and 60 to 105 ms on Streamed Terrain, where the frame itself is 260 ms. A real graphics card hasn't been measured.

Choices made (the developer may change them): a screen doesn't check whether a wall hides it from the player; one drone a seat, the nearest on its channel, with no key to cycle them; an owner's drone answers only to that owner's seats, or an unowned one to anyone; the pilot's view always takes over unless third person is on; a seat that drives its own vehicle and sends does both with the same keys; a screen takes the nearest camera on its channel with no range limit; the picture has no static or signal loss.

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
- **Reach.** A part wider than 3.2 m a side (`ConstructEdits.BigPartHalf`) is reached at the point of its bounds nearest the eye, not its centre (`ConstructEdits.ReachPoint`): the attach tool, paint, configure tool, F and the look ray agree, and you use a 10 m machine from beside it. A smaller part keeps the old rule.
- **Spawning.** A part this size spawns clear of the player's corner distance and lifted out of the ground (`SpawnInFront`). The creative physics gun lifts up to 60 t; in survival the cap stays 500 kg, so a machine goes on by the attach tool or stays where it was built.

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

- **The part** (`Constructs/Parts/Assembling`, attach tool, physics-only, five tiers: [Machine tiers](#machine-tiers)): an open gantry 2 to 6 m wide, with a low hopper at the back (its input), a tray at the front (its output) and an arm. `storage.assemble` holds `capacity` (the fill's scale), `speed`, `chuteLoad`, `batches` (how many batches of each input it buffers: 4 a tier), and `outAt`, `outDir`, where a body leaves. Its `slots` are the tray.
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

**In survival ([Creative or Survival](#creative-or-survival)).** The Assembler is, with salvage, how parts come into being: nothing comes from nothing. Tier 1 machines ask only for what a tier 1 Assembler can make, but it can't start from nothing: a first Assembler, Foundry, Smelter and Sawmill come with the start kit (or the map). A Battery needs circuits, circuits need gold, and gold needs tier 3 machines, so the first Batteries come from the map too, or from circuits the Recycler gives for wrecked electronics. In survival the physics gun stops at 500 kg, so a built machine stays where it left until the attach tool fits it.

**Self-test**: "Assembling" and "Assembling network" (`Source/App/SelfTestAssembling.cs`): the table and its refusals; the size table, aisle and solid share; ore to metal and stone to steel, into an assembler by real chutes with a shelf of parts, an Engine leaving as a body; missing input, blocked output, power, Run, tier gate, a refused chute; items on the tray and down a chute; F; reach beside a Plasma Smelter; creative; allocation; a real host, a client and a late joiner. "Refining" drives the Foundry.

Choices made (the developer may change them): the recipes and every number; no named input ports (one port takes every chute, and the input sorts itself); physics-only parts can't be inputs, so an Engine can't go into a Monster Truck recipe; a machine buffers 4 batches a tier; one recipe a part and one output a recipe; outputs fit in a slot or leave as bodies; a parts input isn't drawn, only a level; a batch started under one recipe restarts if the recipe changes; generated parts from the weapon builder have no recipes.

## Physical storage (decision 18)

The developer's decision 18 (2026-10-29, "Inventories"; board item dev-sandbox-physical-storage-ps02): creative keeps the
inventory window (above); survival keeps things physical. Storage parts are construct parts with **slots** you set parts
into in the world, and bins that hold bulk material with a visible fill level. There is no storage screen: the only UI is
the look-at tooltip. It works on every Sandbox map (the parts are parts), so the yard's creative players can use it too.

### The parts (`Constructs/Parts/Storage`)

- **Chest** (attach tool): two medium slots inside an open steel box. The **Chest** blueprint adds a **Lid Hinge** (a long,
  flat motorised hinge, `power` 0) on its back edge and a **Chest Lid** on that: F on the lid opens and shuts it. Shut,
  the lid is in the way: nobody sees into the chest or takes anything out (the look ray meets the lid).
- **Parts Shelf** (attach tool): two big slots below (an engine fits), three **parts bins** above, each piling up to six of
  one small part (a pile of lamps, of buttons...).
- **Wheel Rack** (attach tool): three slots that take only wheels, standing upright (a slot's shape).
- **Materials Bin** (attach tool): an open bin of 600 units of **one bulk material at a time** (any; the first deposit sets it), its level rising in the material's colour.
- **Recycler** (attach tool): see [Salvage haul](#salvage-haul-decision-31).
- **Storage Volume** (attach tool): a box of interior space you size yourself, up to 40 m a side, for one bulk material or stored parts: see [Storage volumes](#storage-volumes).
- **Shelf Bays** (attach tool): modular racks that snap together: see [Modular shelves](#modular-shelves).
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
| `rack` | `true`: a [shelf](#modular-shelves) slot: it takes as many of one kind as really fit it, at most 48, in rows and layers. No `stack` with it. |
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

- Each storage part holds a handle (`CPart.Store`) into the world's pool of stores (`ConstructWorld.Stores`, 1,024 of them,
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

The developer's answer on carrying (2026-10-30): the hands plus a visible backpack of about
eight slots of different sizes, still no inventory screen. Every player in survival wears one (`ConstructEdits.Packs`, Source/Constructs/ConstructBackpack.cs). It isn't an item you pick up: it comes with the mode. **Creative has none** (the user, 2026-11-05):

- **Creative** means endless parts (`SandboxInfiniteParts`), the same answer on every peer. No pack is drawn, no slot strip shows, and nothing goes in: Stow and Draw say "no backpack in creative". With full hands, `Give` skips the pack and drops the part as a loose pickup; taking from a slot or picking up a loose part is refused and it stays put.
- **An old creative save** that holds parts in a pack keeps them: the player wears that pack (`ConstructEdits.WearsPack`) and draws from it until it is empty, and then it is gone. Nothing new goes in.
- The **Backpack part** in the Tab inventory stays a storage part to bolt on a construct. It isn't worn.

In survival:

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

## Modular shelves

The user's "a modular shelf so the player can stack and store loose parts". Parts: `Constructs/Parts/Shelves`, written by `tools/constructs/shelf_parts.py` (with the Smart Terminal and the **Smart Depot** blueprint). Recipes in `tools/constructs/assembling.py`, tier 1, planks and a little steel.

- **The family.** A **Shelf Bay** is one board on four posts, open at the front and top, on its class's lattice, with sockets on every face but the front, so bays stack up, run along and stand back to back, and bolt to any frame of their class.

| Part | Class | Size (wide x high x deep) | Shelf slots |
| --- | --- | --- | --- |
| Shelf Bay 0.5 | small | 0.5 x 0.5 x 0.5 m | 1 |
| Shelf Bay 1 | small | 1 x 0.5 x 0.5 m | 2 |
| Tall Shelf Bay 1 | small | 1 x 1 x 0.5 m | 2 |
| Medium Shelf Bay 1 | medium | 1 x 1 x 1 m | 1 |
| Medium Shelf Bay 2 | medium | 2 x 1 x 1 m | 2 |
| Medium Shelf Bay 4 | medium | 4 x 2 x 1 m | 4 |

- **How a part is held: in a slot, frozen to the shelf.** A part on a shelf is not a body. It is a count in the store, drawn where it stands (`"rack": true` on a slot; `ConstructStorage.PileShape`, `StackOf`, `ItemOrigin`). So a shelf on a truck that rolls over keeps its stack, a full wall of shelves costs the physics nothing, and every store rule works as it does (chutes, beacons, the look line, spilling, saves, the wire). The price: a stack doesn't topple, and a shot hits the pile, not one piece in it.
- **A visible stack.** A shelf slot takes as many of one kind as fit, from the part's box: rows across the floor, the back row first, then layers up, in the turn that packs most (at most 48 a slot). A Lamp, 20 cm a side, stands 8 to a Shelf Bay 1 slot. The ghost shows where the next one goes. One kind a slot: "that shelf holds Lamps", "that shelf is full (8)".
- **Setting a part on.** Click a lit slot, as on any storage part; or let go of a loose part over a shelf, by a drop from the hands (RMB) or the physics gun's release: within 0.8 m above a board it goes into the nearest slot that takes it (`ConstructEdits.Shelve`, run on the release, never per tick). What a shelf won't take (physics-only pieces, a part too big, a slot taken) stays a body and comes to rest on the board as on any floor.
- **Chutes and beacons.** A bay is a store: a chute feeds it and takes from it, and a beacon or terminal counts it.
- **Cost at scale.** A look or a shot meets a pile as one box (`ConstructStorage.PileBox`), and past 30 m a pile is drawn as one box too (`ConstructRender.FarPile`). Stores went from 128 to 1,024 handles, as a wall of bays is a store each (the look is what walks them, and chutes; nothing else scans the pool per tick).

Choices made (the developer may change them): slots, not bodies on a board; small and medium only (items are at most 40 kg, so a Large bay's 8 m floor would hold a person's parts out of reach, and physics-only pieces don't go on shelves); one kind a slot; no mass for what stands on a shelf, as for all storage; a pile keeps only its top part's health; a Medium bay's 4 m size for pallets of small parts; let go within 0.8 m.

## The Smart Terminal

The user's "some way to interact with a chute system in an area, so a smart inventory system could pull an item from connected inventory volumes". Code: `Source/Constructs/ConstructTerminal.cs` (the view, the rules, the networks' side, the server's pulls, `Shelve`), `Source/Game/GameSandboxTerminal.cs` (views per player), `Source/App/Program.SandboxTerminal.cs` (the keys and the list).

- **The part** (`Constructs/Parts/Shelves/SmartTerminal.json`, attach tool, physics-only, 70 kg, 60 W): a cabinet with a lit screen over a tray, a storage part with `"terminal": true` and its tray as a `rack` slot. It needs power in survival, draws only while on, takes a **Run** wire, and stays in place wrecked, with what is on its tray. Its lamp lights while it works.
- **Two chute ports.** **Network** (both ways) reads: any intact chute on it, either way round, joins the terminal to a chute network, and it moves nothing. **Chute out** (out only) carries what was pulled away, and what it feeds is not on the network by that chute. A chute into a terminal brings nothing in. So stores may hang off it in a star (link from Network to each), or in a chain that runs away from it (Network to shelf, shelf to volume), and nothing drains out of them. In a blueprint: `"fromPort": "Chute out"`; a chute that names none takes Network.
- **The network** is the beacons' (`ConstructBeacons.Graph`): a terminal is a node of the same union-find, so it reaches junctions, world links and cargo hoses, and stops at a cut. Its stores are the same kinds (`INetworkStore`), with the same contested rule and owner rule (yours, or nobody's). Its tray isn't a store of the network. It doesn't need a beacon, and works in creative and survival: it moves real stock either way.
- **The list, on its screen.** No window and no cursor (the user prefers no menus). The terminal has a [display panel](#display-panels) (`"source": "terminal"`, six lines, drawn out to 16 m): stand within 4.6 m and it shows the network's list: `Item 3/12`, four rows round the picked one, materials first, then parts by name, each with its count at the right edge and the pick lit and marked `>`, and a last line for what it is doing: `Lamp 2s +1` (seconds until it lands, pulls waiting behind it), `100 metal waiting`, `Tray: Lamp x2` or `Ready`. Look at it within reach: the wheel moves the pick, and the look line reads `Smart Terminal: 3 stores, Lamp x8 - Wheel: pick - F: to hands - R: to chute`. Off, the screen says why; further off than 4.6 m it shows its stores and parts. A wrecked terminal's screen goes dark.
- **Each viewer has their own screen.** A panel is drawn on each peer for that peer's player, and the list goes to one player only, so `ConstructTerminals.FillPanel` reads the local player's `TerminalView` (`ConstructWorld.LocalTerminal`, which `Game` sets) when it is about this terminal. Two players at one terminal each see their own list position and pick on the same screen, and neither moves the other's. The pick lives in `TerminalView` on that peer (`Picked`, `Step`) and is never on the wire. What a pull is doing rides in the terminal's store, so every peer's screen shows that. Anyone not at it sees the totals, from the store too.
- **The data side stands apart.** `TerminalView` holds the terminal's handle, its stock (a `BeaconView`), `Rows`, made again only when the stock changes, and the pick; a row says what it is (`TerminalRow`: part or material, id, name, count). `Program.SandboxTerminal.cs` is only the keys.
- **A pull** (`CActKind.TerminalPull`, `ConstructEdits.TerminalPull`; the server decides). It finds the store nearest by chutes that holds the item (`NearestStore`: hops relax over the chutes the graph kept, on the request, never per tick) and waits a chute move per hop, one second each, so a part two hops off takes two seconds; a terminal works through its pulls in turn, at most 8 waiting. Then it takes the item from the real store, so nothing is ever in flight to save or lose: a pull under way is simply forgotten by a save. If that store has lost it, the next nearest serves; if none has, "no Lamp left on the network".
  - **F: to the hands**: `Give`'s rules (hands, backpack, ground) if you are still within 8 m; further off a part lands on the tray.
  - **R: to the chute**: a part lands on the tray in its pile, where a chute on Chute out takes it on, or F takes it, as from any slots. Nothing can be set on a tray. A part too big for it is refused ("pull it to your hands"), and a full tray leaves the part where it was.
  - **Materials**: F fills what you carry up to a load (100). R needs a chute on Chute out: up to a load waits in the terminal for the chute (`CStore.Amount`), then goes as from a bin.
- **When it is worked out.** Never per tick. The graph runs on a change, the counts when a store changed, at most at 10 Hz (`ConstructBeacons.Step`), with beacons or terminals in the world. A player's list is made at 4 Hz, only while they stand within 4.6 m of a terminal, and only if something changed; it walks only that network's stores. All of it is fixed arrays: up to 64 terminals, 64 networks, 32 pulls under way, 2,048 chutes kept for hops (past that a store counts as one hop). Nothing allocates in a steady tick; the rows are made on a change, on the peer that shows them.
- **Network (protocol 142).** `CBodyKind.Terminal`, to its player alone, only on a change: u16 the terminal's store handle (0: none), then a beacon message's bytes (`ConstructBeacons.Write`: bounded, in turns; it is the same delta). The look line's numbers ride in the store, as a beacon's do. A store's second flags byte gains 8 (protocol 149): what a terminal is pulling (u16 id, u8 is-a-material, u8 pulls under way, u8 seconds), sent when one starts, ends or a second goes. `CActKind.TerminalPull`: u16 body, u16 part, u16 id, u8 flags (1: to the hands, 2: the id is a material).
- **Saved and copied.** A terminal, its ports and chutes, and what is on its tray save with the world; shelves save their piles. A blueprint carries the parts and chutes, Chute out by name, and comes empty, as every store does. A joiner hears the stores; a joiner's list goes whole the first time they stand at a terminal.

Choices made (the developer may change them):

- A list of four rows at a time on a six-line card, cut to the screen's 18 characters a row (the look line has the full name); the keys wheel, F and R, not a click, so any tool can be out.
- Each viewer's own pick on their own screen; a pull's state is shown to everyone.
- A pull reads through a chute either way round, as a beacon does, and against the way it runs: the wait is the chutes between, not their direction.
- A pull takes a chute move per hop however long the chutes are; one at a time per terminal; 100 a pull for materials, as F on a bin.
- A pull's item stays in its store until it arrives, so another player or a chute can take it first; the player hears it.
- No filter on Network: a terminal reads every store on its network. Connecting a chute to someone else's store exposes it, unless they stand defending it, as with a beacon.
- One terminal shows its list only to players standing at it, and one list at a time, the nearest.

## The Inventory Beacon

A build aid for survival, and the deliberate exception to decision 18: the developer asked for a beacon that, "when connected to
a chute network, will broadcast all the available items so the player can access them as if they were using creative mode, but in
survival", and that takes parts back to the network rather than the hands. Code: `Source/Constructs/ConstructBeacons.cs` (the table, the
networks, the views, the message), `ConstructEditsBeacon.cs` (the server's takes and returns), `Source/Game/GameSandboxBeacon.cs` (views per
player, the HUD), `Source/UI/UiSandboxBeacon.cs` (the window).

- **The part** (`Constructs/Parts/Storage/InventoryBeacon.json`, attach tool, physics-only, 90 kg, 120 W): a cabinet with a screen, a mast with
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
- **Smart Terminals** ([The Smart Terminal](#the-smart-terminal)) are nodes of the same graph, with their own list; a terminal's Chute out joins nothing, and a terminal's tray isn't a store here.
- **Adding a store kind.** `INetworkStore` is all the beacons know of a store: `Serves`, `Parts`, `Tally`, `Holds`, `TakePart`, `TakeBulk`, `PutPart`,
  `PutBulk`. Add one to `ConstructBeacons.Kinds`, before `SlotStore` if both would serve the part.
- **Network (protocol 116).** `CBodyKind.Beacon`, to its player alone, only on a change: flags (1: start empty), state, stores, parts, bulk, range,
  distance, then (part id, count) for up to 240 kinds that changed and the materials that changed: about 1,100 bytes at most, in one reliable
  packet. What doesn't fit goes next tick, from where it stopped; leaving a network sends the state alone. A joiner's starts empty. A store's flags byte gains 8: a
  beacon's stores and parts follow (u16 each), for the look line on any peer. `CActKind.BeaconMaterial` (u8 material).

**Self-test**: "Inventory beacon" and "Inventory beacon network" (`Source/App/SelfTestBeacon.cs`): the table and part; the depot on the real step: chutes, a junction, a
world link, two beacons, two networks; no graph or count run and no allocation over 240 steady ticks; range, ownership, power, Run, a wreck; the real take, held part
returned, a physics-only piece, the contested last one; bulk both ways; a row refilled; a part taken off into the network, a full network into the hands, a
attached piece a body; a cut chute; the message's bound; and a real host, two clients and a late joiner, with the window headless.

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
machine you attach to a base, power and chute to your stores; in its range it gives one building aid. Survival needs the stations; away from the
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
  Short: it places what it can and says how many are missing. Aiming MMB at a physics-only part with a mode on (or clicking one in the inventory) picks it for the aid; it needs the attach tool.
- **Mirror (M: off, side to side, front to back, up and down; Page Up / Down: move it).** The plane is the table's `mirrorAxis` and `mirrorOffset` settings (the configure tool shows them too, and they save with
  the part); in creative with no table it is the player's own. It is measured from the built-on construct's drive seat, else its root part, square to that part's axes, so one table serves every construct
  in range. A fit or a removal also happens to the image, in the same server action; a part across the plane is its own image. The image's turn is found by shape (`ConstructMirror.TurnOf`, cached):
  of the 24 turns of the part and of its Left / Right partner by name, the one whose boxes and sockets land on the reflected ones. A part with handedness and no partner takes the nearest turn and the HUD says so; a side with
  no free socket says so. The image of a stroke is not mirrored again. freeform-attached pieces aren't mirrored, and neither are the attach tool's socket and point attachments: only the held-part fit, the Line Layer and removal.
- **Line and fill (O).** Press on a ghost and drag: the run lies in the rest-frame plane square to the view at the press, along its two axes, so a straight drag is a row and a diagonal one a field (at most 400 cells).
  The pitch is the next matching socket along the row if there are sockets to follow, else the part's own opposite sockets, else its size. Each cell goes through `ConstructAid.FindFit` (a pose to a free socket, by the rules of a click), in order, so a cell lands on an existing socket
  or on the cell before it. Nothing is sent until the release, so a press with no drag is one part and a stroke never acts on what it made (the `SnapStroke` lesson); RMB cancels. One `BuildAidOp.Line` a stroke. The start part is stamped with its kind and place. The look line shows the run, what is short and the mass; the ghosts are made again only when the run changes.
- **Replace (P).** Aim with a part in hand (or picked) and click; with none, a click opens the picker: every part of the size class that fits, with what you have. The part keeps its index, so joins, wires and links stay;
  a link moves to the new part's matching socket, a routed link's port to the same name, else type and way. Its setting values carry over by key, its paint stays, its health stays as a share. `ConstructReplace.Plan` says why not: a connection that doesn't meet, something in the way, a store with something in it, a link with nowhere to go.
  It refuses joints that carry something, merged or builder receivers, volumes, hoses, freeform attachments, couplings and world links: take those off and fit the new one. The old part goes back as a part taken off does; a physics-only one is set loose above its place.
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
| LMB, drag from open ground | A box on the world, from just under the drag to 48 m above it, for mass selection: a construct whose middle is in it comes whole, one that only reaches in comes with the parts whose middles are. Shift adds. The ground is never selected or saved. Only what the player may delete, within reach |
| Double-click | The whole connected section: every part reached over links without crossing a joint |
| RMB, drag | A rectangle on the screen from the crosshair; the view holds still. It takes the parts whose middles are in it. Shift as above |
| RMB | Clears the selection |
| M, O, P, U | Move, copy, paint (the paint tool's colour; Reset resets), attach. The stations' switches on these keys are off while the selector is out (`Keybinds.Overlap`) |
| Delete, twice | Deletes the group |
| Comma | Saves it as a blueprint (needs a live Desk) |

**Placing** (move, copy, stamp): a ghost follows the crosshair and rests on what is aimed at, or floats at a distance (the wheel). R turns it, the arrows tip it (`GhostTurn`), LMB places, RMB or Esc stops. Aimed at a part
with a free structural socket on the construct's body it snaps there: the clip mates by its own free sockets, outermost first (K: the next one; R: the next spin). A copy or stamp stays up to place again. What lands free is **frozen
where it is** (`Construct.Frozen`, as the physics gun freezes), so a group doesn't fall as it lands; the physics gun lets it go. What lands on a socket is attached on (`ConstructPlace.FindJoin`, `ConstructWorld.Merge`). A selection in pieces
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

**Time.** Selecting and painting are instant. The one structural edit is one tick; checks, payment, refunds, joins and a blueprint's build run as jobs of `ConstructEdits.SelectTick`, 2 ms a tick over all players. A big group's join links through the part tree (`AutoLinkMany`), so joining stays linear. Warm on a
7,921-part hull, 2,000 parts on the 2-core container under load: the request about 1 ms, the commit about 2 ms, the rebuild about 1.5 ms, `WriteFull` under 1 ms. A ghost draws its first `MaxGroupBoxes` boxes and a frame round the whole; the selection outlines its first `MaxOutlines` parts.

**Blueprints** go in the save folder's `ConstructBlueprints` folder, one file each, named after the blueprint (letters, digits, spaces and `- _ ( ) . '`, at most 32; a name in use asks). A file is a real blueprint in the shipped format, and goes by part name, so it can be dropped in `Constructs/Blueprints`. `ConstructClip.ToBlueprint` writes it: a step per part in the clip's order, on the
A saved blueprint records what is, not what the rules would make now. Each step has an `"at"` (the part's place, relative to step 0), so a part stands exactly there even if its socket link is 2 cm out or it overlaps a neighbour; its links are written out too (`"exactLinks": true`, and a step's `"also"`: `[step, their socket, its socket]` for each further link to an earlier step), and a file with `"exactLinks"` makes no others by parts landing on free sockets, because a clip's order isn't the order the parts went on and a free socket here could take one a later step needs; each link has `"laid": true` and `"hosts"` (each node's step), so only its ends and lengths are checked, never its route again, and a blueprint link can carry `"paint"`. A shipped blueprint needs none of these. All of a link's points are relative to step 0, because a selection doesn't start at the construct's origin. A link with an end outside the selection is left out, and the save window says how many; a refusal names what can't build.
earliest step it hangs on, by the sockets they meet at; a freeform attachment is a `"weld"` step, and a piece no link joins to the rest has no `"socket"`: it just stands at its `"at"`, and lands as a body of its own. A save is built back from its own text before it is written, and must give every part. Save needs only the client's copy, so it is a client-side action; the Desk check there is the client's. All 46 shipped blueprints without cargo round-trip. Contents and merged receivers' gun parts don't go in a blueprint (the save says so).
The Tab inventory's Blueprints tab lists them above the shipped ones, with a filter, Stamp and Delete (a survival inventory shows only these). A stamp goes up to the server as deflated text in `UploadChunk` pieces; the server parses it off the game thread, builds it step by step on a scratch world through the normal rules (`ConstructBuild.BuildSteps`), and refuses a part it doesn't have by name.

**Wire**: `CActKind.BuildAid` with `BuildAidOp.Select`, then a `Game.SelOp` (`GameSandboxSelect.cs`). Server: `ConstructEditsSelect.cs`. Client: `SelectController.cs`, `Program.SandboxSelect.cs`, `ConstructRenderSelect.cs`, `UiSandboxBlueprints.cs`.

Choices made (the developer may change them):

- A selection is one construct, unless a box from open ground or Shift reaches others (at most 256). On several, paint, weld and delete go to each in turn; move, copy and save say they need one construct.
- Free landings freeze; attached ones don't.
- Nothing on the wheel: a tenth segment moves every tool on it.
- Physics-only parts are paid in loose bodies near the player.
- A stamped blueprint's fuel and charge are what fitting new tanks and batteries gives (full), as for any blueprint.

## Construct weapons (milestone 3)

Decision 10: construct weapon parts like the Zombies weapon builder's, but assembled in the world into fixed or turreted
guns on vehicles and bases. Board item dev-sandbox-m3, part 1 of 4 (the developer approved it 2026-10-29). Everything is
a part fitted with the **attach tool** like any other (no workbench, decision 2), and every part's weight counts.

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
  A [Turret Module](#control-modules) linked into it is such a wire: its seat's sitter fires it with their fire key. A
  seat with no module fires nothing, wherever it sits. Every shot fires each barrel's pellets at once, at the
  gun's rate; semi-automatic and burst actions fire at their rate while held (no trigger discipline on a mount).
- **Its aim.** A **fixed** gun (on the body) points where its construct points. A gun on a **turntable** (and on a **motor
  hinge** for elevation) is pointed by the **look** of the gunner in the seat on its Turret Module: every turntable and motor hinge up the gun's chain turns,
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

### Flame and harpoon

Two fire types of the one modular weapon system (`Source/Constructs/ConstructHarpoon.cs`, `Source/Game/GameSandboxHarpoon.cs`; the
builder's side is in docs/zombies/WEAPON-BUILDER.md, "Flame and harpoon"). They are weapon builder parts, so each is a handheld gun
at the workbench and, generated like every builder part, a gun on a construct: bolt a barrel and a feed to the **Firebreather
Receiver** or the **Whaler Receiver**, link a [Turret Module](#control-modules), and a seated gunner fires it. The hand-made
Flamethrower heavy gun stays as it was. Numbers are in `Constructs/WeaponParts.json`.

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `flameReach` | How far a flame goes at all, as a share of its full-damage range (1.25). |
| `flameReachPerStep`, `harpoonReachPerStep` | Range added per step of a merged receiver above 1x1 (0.5: a 2x2 has 1.5 times the reach, a 3x3 twice). A merge's own `range` power is 0, so this is what gives a big receiver its reach. |
| `flameRoundsPerLitre` | Rounds of fire in a litre of the construct's fuel (5). |
| `flameArmourHalf` | The armour at which flame does half (10). |
| `harpoonReel`, `harpoonHandReel` | Reel speed on a construct (4 m/s) and in the hand (3 m/s). |

**Flame.** A flame gun is a pellet gun with an end: 4 licks a pull, 8 pulls a second, each an ordinary `Round` (one ray against the
map, `ConstructWorld.Raycast` and the targets, in fixed arrays), so a flame adds no per-tick allocation and a tick's cost is the
licks fired, at most 32 rays a second a gun. The ray stops at `MaxRange` (hand: `WeaponDef.Reach`, also in the client's prediction).
Each lick lights what it touches through the same `Ignite` as incendiary rounds (wood, rubber, cloth, fuel tanks, ammo boxes: 12 s of
burning, spreading half-way), including a hand gun's now: `SandboxShotDamage` used to ignore incendiary rounds. A cut fuel pipe under
fire lights its tank (`ConstructRoutes.Damage`'s `blast`, now also for fire from any incendiary round). Heat isn't stopped by armour
as a small bullet is: `damage / (1 + armour / flameArmourHalf)`, raw. Rays that reach the map go on to `ShotProps` (`FlameWorld`), so
a mounted flame sets off explosive props as a hand gun does. Players burn as from any incendiary round (`IgnitePlayer`).

| Flame | Lick | Pulls a second | Full damage to | Reach | Rounds a pull |
| --- | --- | --- | --- | --- | --- |
| Hand or 1x1, plain | 4 x 4 | 8 | 8 m | 10 m | 1 |
| Hand, Napalm Gel | 5 x 4 | 8 | 7.2 m | 9 m | 1 |
| 2x2 merged | 16 x 4 | 4 | 12 m | 15 m | 4 |
| 3x3 merged | 36 x 4 | 2.7 | 16 m | 20 m | 9 |

**Fuel.** A flame gun loads from the ammo boxes its feed reaches, like any gun, and then from the construct's own fuel
(`ConstructHarpoon.TakeFuel`): 5 rounds a litre, so a truck's tank is its flamer's magazine. The look line counts both.

**Harpoon.** One barbed head, a hitscan round drawn flying at 80 m/s (the wound lands at once, like a cannon shell). Where it strikes it
ties a real rope (`ConstructWorld.TieLine`: a `CConnector` with `Line` set; launcher at A, head at B): a construct's part, a wreck, a
loose body (all parts, tied by tag, so a part that breaks takes the rope with it), the map (a world end, which goes when the ground does), or a player. A miss ties nothing; the
head is lost. Reach is its range: 40 m, 60 m on a 2x2, 80 m on a 3x3, at most `ConnectorMaxLength`. The rope behaves as any rope does: it
holds and drags through the contact solve, and snaps over its rating with the crack, whip and word to the players near.

| Launcher | Rope |
| --- | --- |
| Hand | S (1.5 t) |
| 1x1 receiver | M (6 t) |
| 2x2 | L (25 t) |
| 3x3 and up | XL (120 t) |

- **One line at a time.** While a line is out the launcher can't fire. **Fire reels it in** (a new press: the pull that fired it doesn't
  reel); **R cuts it**: the seated gunner's reload key, or the hand gun's. The attach tool's RMB and the remover take a line off as any
  rope. A wired trigger reels too; a wired gun has no key to cut with. A reel stops at 1.5 m of rope, and stalls with 1.5 m of stretch when both
  ends are held, so it doesn't tear its own rope. Survival: a shot costs its round, not the rope's wood (the rope is in the head's price).
- **An end on a player.** `ConnectorEnd.Player` (id + 1): a world end each PC keeps on the player (the server each tick, each PC each
  frame as drawn), so drawing, aiming at the line and the list work unchanged. The solver can't move a player, so the shared
  movement step does (`Game.TetherPlayer`, on the server and in the client's prediction): past the rope's length plus 0.6 m of give a player is
  drawn back along it, up to 14 m/s, staggered so the ground's grip doesn't undo it; the mover keeps them out of walls. That give's
  stretch pulls the body at the other end, softly (`PlayerPullStiffness`, 2.5 kN/m: about 1.5 kN at the most): a crate follows a player,
  a parked truck stops them. Riders on a construct aren't moved: a line held from a moving deck tears when it runs out.
- **A player hit** takes the head's damage (45, 50 with a Barbed Head, which also bleeds), and is held on the line: they can't walk past
  it, and reeling hauls them in. **F tears it out**, for 10 damage (never fatal); they go free as well when the launcher lets go,
  dies, leaves, or puts the gun away, or when the rope snaps or tears.
- **Saves.** A line on a player isn't saved. One between constructs loads as a plain rope (the gun can fire again).
- **Not in Sandbox** a Whaler is a plain heavy bolt: no rope.

Wire: protocol 148 (a connector's two player bytes, `CActKind.Harpoon`: 0 cut, 1 reel, 2 stop, 3 tear out).

### Ammo feeds

Code: `Source/Constructs/ConstructAmmo.cs`; generator: `tools/constructs/ammo_parts.py`. A vehicle's ammo can sit in one armoured place, with feeds running
out to its turrets.

- **What sets a mounted gun's rounds.** What feeds it. No part on a mounted gun sets the type any more: Ammo parts on a gun (FMJ, AP, HE and Incendiary Rounds
  and the builder's) do nothing there, though an old one is left in place and a box bolted to it still feeds. Handheld guns are unchanged: the workbench's Ammo socket.
- **Ammo type.** A setting on Ammo Boxes, Crates and the Ammo Container (configure tool; default FMJ). A type is one of the weapon builder's Ammo parts
  (`ConstructAmmo.Types`, an index on the wire and in saves, so new ones go at the end), so its stats and flags go through `BuildStats` unchanged: Armour Piercing penetrates,
  Dragon's Breath lights, Explosive Tip splashes, a Siege Shell digs deep. Ammo stays universal: **any type fits any gun**. Calibre comes from the receiver's bore and the
  store only says the round type, so one crate feeds a rifle receiver and a howitzer alike. (A flame or harpoon gun takes the type's numbers but stays what it is.)
- **Loading.** A gun loads at its reload, from its feed's store first, else a magazine on a socket, else its own magazine, else the nearest box bolted to it with rounds in it, and only that type. Its numbers change there
  (`BuildGuns`, allocating once at the change). A different type clears what was left in the gun. A seated gunner's **reload key** clears it now, losing those rounds, to take what a switch has chosen.
- **The Ammo Container.** A Storage Volume's box you size with the configure tool (Weapons > Ammo boxes), the same code, so handles, saves, the wire, sealing, spilling and the fill drawn
  come with it. It holds the bulk material `ammo`, 400 units a cubic metre, 10 rounds a unit, 1 kg a unit: 4,000 rounds a cubic metre. Sealed-volume rules apply as to any
  solid: each open face costs a sixth of 10% of its capacity, it pours under hard acceleration, and `ammo` is on `Sealing.json`'s flammable list, so an open box burns. Bulk ammo comes from a Foundry
  (1 steel + 1 bronze: 8 ammo) and arrives by chute or tipped in; creative sets `fill`. Only ammo feeds guns, though it takes anything a Storage Volume does. Holed to nothing it blows up by what is
  in it (3.5 m + 0.25 x the square root of the units, at most 12 m; 260 + 20 x that root, at most 1,500).
- **The ammo feed.** A new link type in the linking tool, `RouteType.Feed` ("Ammo out" to "Ammo in"): a flat belt with a yellow stripe, 60 health a segment, routed, auto-routed, branched, loomed and carried in
  trays like any link. Like a chute it may cross a moving joint as a flexible section, so a feed reaches a gun that swings through a limited arc. Every gun has an implicit "Ammo in" (at its magazine socket, "left") and every box and crate an "Ammo out"; a
  cut feed delivers nothing. Hits on it are shielded by trays as for any link. A flexible section has the flexible limits: at 135 degrees of bend it stops delivering, past about 169 it tears. Measured through a real host and client at 80 ms with 15% loss, firing: a feed across a turntable and a motor hinge feeds through +-80 degrees, stops, and tears at about 182 degrees of traverse. For a gun that goes all the way round, use a reel.
- **The Ammo Reel.** Ammo Reel (8 m) and Large Ammo Reel (16 m), parts in Weapons > Feed chutes, like the data lead reels (`PartRole.Hose`, `HoseCarry.Ammo`, `Source/Constructs/ConstructHoses.cs`). Feed its "Ammo in" with a feed link, or bolt a box to it, take the belt by hand (F) and plug it into an **Ammo Socket** (F), bolted to the gun or wired on with a feed into the gun. The belt is a rope: it follows the gun however the turret turns, on one construct or across two, and comes out only past its length. Differences from a laid feed: no bend limit, no routing or trays, no health, nothing to hit, so no cook-off, and it is plugged by hand. Measured the same way: a full 360 degree traverse, firing all the way round, with the rounds drawn from the crate. A blueprint plugs its reels with `"hoses": [[reel, inlet], ...]` (step numbers).
- **A gun's magazine.** The Feed category's parts are now called **Magazines** (the construct menu and the workbench alike; hand-made Belt Feed and Shell Feed keep their names, as blueprints name them). A receiver holds one on its magazine socket ("left" on a generated receiver). Early guns are reloaded by hand: hold an empty magazine in your **Hands**, press F at a box, crate or Ammo Container and it fills, the store's type and all, out of that store's rounds; fit it with the attach tool as any part; a gun takes its rounds at its reload. Take the empty one off with the remove tool and go back. It keeps what is left in it, wherever you carry it (below). The rounds and their type sit in the part's `Rounds` (`ConstructAmmo.Pack`), so there is no change on the wire or in saves. In creative one comes full of FMJ. A merged gun takes N squared rounds a shot from a magazine of the same size, so one lasts a quarter or a ninth as long.
- **A magazine on an Ammo Socket.** For the early game, before there is a crate, reel or belt: a magazine clips onto any free face of an Ammo Socket (the attach tool's held-part path, with a ghost), on a socket bolted to the gun or one with a feed link from its "Ammo out" to the gun. The gun needs no magazine of its own, draws its 3 s of ready rounds from it at `feedReload`, and takes the type of the rounds in it. When it runs out, take it off with the remove tool, or hold a filled one of the same kind and press F on the fitted one: they change places. A magazine a gun can't take (a shell magazine on a rifle's socket) is refused with the workbench's reason, on the HUD and at the attach (`ConstructAmmo.GunTakes`, `SocketRefusal`); one already on a socket when a link goes in is ignored by that gun.
- **The order of a gun's sources.** A working feed with rounds behind it (a link, or a plugged reel), then a magazine on a socket along that feed, then the magazine on the gun, then a box bolted to the gun. Why: what is brought automatically should run first so the hand-carried rounds stay in reserve for a cut belt, and the socket's magazine comes before the gun's own because taking it off doesn't touch the gun. A gun with a magazine on it keeps that magazine's reload time even while a feed serves it.
- **Carrying a magazine.** Its rounds and type stay with it in the hands, on a construct, and lying loose (dropped, set down or spilled from a dead player's backpack). Put away in the backpack, the inventory or a store it is only a part id in a count, so the player keeps the rounds: `ConstructEditsMags.cs` notes them by kind, and the fullest of that kind comes back the next time a magazine of it is in their hands. So two of a kind in a backpack hand out their rounds fullest first, and a magazine left in a crate leaves its rounds with the player who put it there. Server only: carried rounds aren't in a world save (the file has no place for them yet); fitted or loose ones are.
- **Feeding a gun directly.** A feed or a bolted Ammo Socket is a gun's magazine's later replacement: with either, no magazine is needed, and the socket is free. The gun draws from the far store and holds only `feedReadySeconds` of fire (3 s: 30 rounds at 600 rpm, at least 1), taking `feedReload` (0.6 s) to draw more. A cut feed leaves only the ready rounds. **A magazine and a feed can sit together**: a working feed goes first, its rounds are the gun's, and the magazine goes in if the feed is cut or its store runs dry. A box bolted to the gun works with a magazine as it always has, and a fitted magazine goes first.
- **Barrels on merged receivers.** A generated receiver's `front` socket sits on the bore (the mean of its builder barrel sockets) rather than the middle of its model's bounds, which side pieces skew, and barrels, muzzles and splitters have front and back on their axis: the Hammerfall Autocannon was 49 mm off, the Bunker Buster 34 mm, the Firebreather 25 mm. A merged receiver scales that by N (`ConstructMerge`, `CentreSocket`), with the magazine socket flush on the merged face at 2x2 and 3x3.
- **The Ammo Switch.** 2, 4 and 8-way parts (Weapons > Feed chutes): feeds into "Ammo in N", one "Ammo out", a data input choosing: **0 the first, 1 the next**, rounded, held at the last.
  A lever works one as a 2-way. Switches chain, 8 deep. The choice takes effect at the gun's next reload.
- **Cook-off.** A loaded feed (its store has rounds; behind a switch, always) that is cut catches fire: from the cut, each way, a segment goes up every `feedCookStep` (0.12 s): a blast of
  `feedCookRadius` (1.6 m) and `feedCookDamage` (70), and the segment is cut. It runs `feedCookRun` (8 m) each way at most and **stops at the end of that link**: a switch, a gun or a store is not crossed, though a
  blast at the end hurts what stands there. So a switch is a fire door. A chain reaches a store only by its blast, and armour and health decide; a store that goes then cooks off as ever. At most 16 burns at once.

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `feedCookRun`, `feedCookRadius`, `feedCookDamage`, `feedCookStep` | Above. |
| `feedReadySeconds`, `feedReload` | What a directly fed gun holds, in seconds of fire (3), and its time to draw more (0.6 s). |
| `roundSpeed` | What a gun's round leaves at (350 m/s, times its `bulletSpeed`), unless its file gives a `speed`. |
| `digDamage` | A round doing this much or more when it hits the ground digs (60). |
| `digRadius`, `digRadiusMax` | The crater at that damage (0.5 m), growing with the square root of it, and the largest (2.5 m). |
| `digPerSecond`, `digGunPerSecond` | Rounds' digs a second: the world's (4, a one-second burst) and one gun's (3). |

### Falling rounds

Every construct round falls (`ConstructWeapons.Round`): at most `ArcPieces` (6) straight pieces of its real path, each cast as a straight round was, at least 40 m long, so a short range is one piece, the
cost of a shot at most 6 times what it was, and nothing is allocated. Speed is `roundSpeed` or the gun's own: an MG's 350 m/s drops about 2.9 m at 200 m. Flames and rockets (self-propelled) fly straight.
A Sandbox hand-held built gun falls too (`Source/Game/GameRoundArc.cs`): its shot is still a hitscan, resolved at once with lag compensation, but along up to 8 straight pieces of the arc, and the shot's usual code runs on the piece that meets something. Stock guns, beams, flames, ropes, launchers and every other mode keep their straight line. A gunner's aim allows for it: the look's point goes through `ConstructShells.Lead`, as for artillery, and the landing cross and HUD line show for every seated gun. Tracers are drawn straight from muzzle to where the round
ended, as the round's end is what the event says.

### Grenades, launcher rounds and cover

`World.Query` is the map alone: constructs aren't in it, so anything that flies has to ask them too. `Throwables.Bodies` is the constructs of the Game ticking (set at the top of each tick, null outside the Sandbox). A thrown thing sweeps each sub-step's motion relative to each nearby body (`Throwables.ConstructContact`), so a thin plate or one coming at it isn't passed, and bounces by the relative velocity. Slow on an up-facing surface it rests on the construct (`Projectile.RideBody`, in that segment's frame): it rolls to a stop, goes with the vehicle, and flies again if it tips over 60 degrees, rolls off or the part goes. A sticky thing stuck to a construct goes with it. Launcher rounds (`RocketFlight.Step`) meet constructs too, and shells always did. A blast's players are covered by a construct between them (`Throwables.ConstructCover`; glass doesn't cover). A construct's own parts aren't: `ConstructWorld.Blast` hits every part in range by distance, through the hull.

### Powder charge

One number for how hard a round leaves, 25% to 300% of the standard load (`Source/Zombies/PowderCharge.cs`). 100% is the gun as it was. **On a construct gun** it is a setting on the receiver (configure tool, `CfgKey.Charge`, group Gun): every builder receiver and the Rifle, Heavy and Shell Receivers have it; on a merged receiver any member's panel sets it (the first member away from 100% counts). **On a hand gun** it is a slider beside the build in the Sandbox's Gun Workbench, kept in the build text's style field after an `@` (`volley@150`), which the default doesn't write. It isn't on a beam (`energy`), `fuel`, `harpoon` or `launcher` receiver, nor on melee weapons. Zombies ignores it, and no other mode has built guns' arcs.

| Effect | Rule |
| --- | --- |
| Muzzle velocity | Times the charge up to the barrel's limit, half as much past it. |
| Barrel | The limit is 100% + 300% x (length in m - 0.2), from 120% to 300%: a 0.25 m barrel 120%, a 0.6 m one 220%, 0.87 m and up all of it. Length is the barrel part's muzzle from its breech. |
| Spread | x(1 + 1.5 x charge past the limit): only past it. |
| Damage | x the square root of the velocity (a shell's blast doesn't change: that's its filling). |
| Recoil | x the velocity. |
| Range | Damage range x the velocity; a mounted gun's longest flight x the velocity above 100%; a shell's reach x the velocity squared. |

So at 25% a rifle round leaves at 88 m/s and falls 1.6 m in 40 m, at 100% it is 350 m/s and 160 m, and at 300% from a long barrel it is 1,050 m/s. The look line on a gun and the workbench show the speed, how far it flies before it has dropped a metre, and the recoil. Rounds are still resolved at once (a shell flies for real); a round under 95% of 320 m/s is drawn travelling, on its arc (`Tracer.Bow`). Everything that aims uses the real speed: a mounted gun's `RoundV` (the gunner's lead, the landing cross, sentries, bandits) and a shell's `Speed`. It isn't wired to a logic port: a gun's one input is its trigger.

### Rounds that dig

A round that reaches the ground with `digDamage` or more digs, through `Game.Dig`, radius `digRadius x sqrt(damage / digDamage)`: a Heavy Receiver's .50 (85) 0.6 m, a 2x2 merge of it (340) 1.2 m. An MG (24) doesn't.
The limit is shared with construct guns' explosive rounds, which dug without one. Each op is 15 bytes on the wire and a line in the save's log, so at the world's rate an hour of steady firing is 14,400 ops at most.

### Artillery

Real shells on a real arc, in the same modular system (`Source/Constructs/ConstructShells.cs`, `Source/Game/GameSandboxShells.cs`; parts in
docs/zombies/WEAPON-BUILDER.md, "Artillery"). Bolt a tube, a feed and a box to a **Field Gun Receiver** or **Howitzer Receiver**, put it on
a Motor Hinge on a Turntable, link a seat through a [Turret Module](#control-modules) as for any gun, and fire. Merge them for more.
They can't be held: the workbench refuses a receiver with the `mounted` flag ("too big to hold").

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `shellBlast` | Blast radius (m) of a 320-damage shell. It grows with the square root of the damage, so a merge's N-squared damage is N times the radius. |
| `shellBlastMax` | The biggest blast (45 m). |
| `shellCrater`, `shellCraterMax` | Crater radius as a share of the blast (0.4), and the biggest (14 m). |
| `shellSiegeCrater` | A Siege Shell's crater against that (1.5). |
| `shellRangePerStep` | Range added per merge step above 1x1 (0.25). |
| `shellKick` | Recoil a shot (1500 N s), times N squared. |

| Piece | Blast damage | Blast | Crater | Shots a minute | Reach |
| --- | --- | --- | --- | --- | --- |
| Field Gun | 320 | 9 m | 3.6 m | 10 | 420 m |
| Howitzer | 512 | 11.4 m | 4.6 m | 6 | 420 m |
| Howitzer, Long Gun Tube, Siege Shell | 819 | 14.4 m | 8.6 m | 4 | 504 m |
| 3x3 merged Howitzer, Siege Shell | 7371 | 43 m | 14 m | 1 | 504 m (420 x 0.8 x 1.5) with a Howitzer Tube |

**Shells.** A gun's reach is its range at 45 degrees, which sets the muzzle speed (`sqrt(range x gravity)`; 87 m/s for 420 m at the
game's 18 m/s2). Gravity is `World.Gravity` with the host's scale, and points at the planet on a curved map. A shell takes the gun body's
speed with it. High Explosive, **Siege** (a deeper crater, slow to load), **Incendiary** (lights what burns in the blast, and burns
players) and **Smoke** (three clouds, nearly no blast) are ammo parts; any other ammo fits, and Napalm Gel makes an incendiary shell.

**Aiming an arc.** The gunner looks where the shell should land: `ConstructShells.Lead` solves the arc to that point and the turntable and hinge
follow, so the crosshair is the landing point; **hold Aim for the high arc**, to lob over a wall or a ridge. The stock Motor Hinge goes to 100 degrees either
way, which is plenty; the Turret Module's `pitch` output (straight down to straight up) works too, for a piece driven by wires. A gun on the body points with its
construct. The drop aid works in every case because it reads the barrel, not the look: each frame `ConstructShells.Predict` flies the arc in half-second steps
against the map, an orange cross marks where the gun would land as it points now, and the HUD line says
`Field Gun Receiver: lands 115 m out, 1.3 s, elevation 7 - RMB: high arc`. It sees the map only, not constructs.

**On the wire and what clients see** (protocol 150). `CFireKind.ShellOut` (id, kind, muzzle, velocity) and `ShellLand` (id, where, radius, kind) go reliably to
everyone in `Msg.CFire`. Each PC flies its own copy with the same step; only the server tests for hits, and its landing event ends the
shell everywhere, so a client's copy runs about half a round trip behind (10 m at 80 ms) and never lands by itself. Both events carry world positions, so
a shot works for a client far from the gun or the target; no interest filter applies to shells, ground ops or blasts. In a big world the far
bodies' poses are what thin out (`WritePosesFor`), so a far target shows its damage as late as its poses. A joiner doesn't get shells in flight.

**Cost.** Everything is in fixed arrays and a tick allocates nothing:
- At most `MaxShells` (64) in the air; a gun holds fire, its round not spent, while the pool is full.
- Each shell tests for what it hit one tick in three, over the whole stretch, so nothing is flown through and 64 shells cost about 21 ray sets a tick.
- One blast goes on at a time, walked `ShellPartsPerTick` (400) parts a tick, over the bodies found when it landed (256 at most). The blast's event, the players, the
  crater and the sound happen at once. A later shell of a salvo waits its turn, about 3 ticks per 900-part target.

Measured on the cloud box: 63 shells at once on a 900-part slab: a mean of 0.1 ms a tick for the guns' step, one tick of 8.5 ms (the slab coming apart), the next 3.6 ms, the rest under 1 ms.

Blasts don't touch props or destructible buildings, as with other construct rounds. Shells aren't saved.

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
- **Taking apart**: the attach tool takes off only the member whose own cell is under the crosshair (the outline is that
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

- **Every builder part is an item.** Every part in `Parts/<Category>` (guns, melee and grenade parts) has a Sandbox
  construct def, so it can be held, stowed in the backpack and storage slots, set down, and fitted to a construct.
  The defs are **generated at load** from the part library, never written by hand: each builder part becomes a part
  file's text that goes through the ordinary parser (so it is checked like any file): its model's boxes (tagged boxes
  left out, each as the box round it turned, the biggest 15 kept) scaled by the rules' `scale` (2: the hand-made
  construct gun parts are about twice the handheld size) and centred on their bounds, a hidden solid box round them, a
  socket on each face (right, left, top, bottom, back, front), the builder's weight times `massScale` (at least 0.5 kg),
  health `healthBase + healthPerKg x mass`. They are fitted as an item and light (at most `ConstructParts.HandCarryLimit`, 40 kg), so
  they are always items, never physics-only bodies (decision 21; the self-test checks it). A receiver becomes a `gun` on that receiver (its stock action, else the
  first action its socket takes) with a **bottom** mount like the hand-made ones; every other part a `gunPart`.
- **Ids.** The generated defs come after the hand-made files, in the builder's own sorted order, so the hand-made parts
  (and saves and blueprints naming them) keep their ids, and every peer with the same files gets the same ids (peers
  check the build on join). A builder part a hand-made file already wraps (the 13 gun parts and the three receivers) is
  not made again. With today's files: 327 generated. A name a construct part already
  has gets " (W)".
- **The inventory.** They are in the Tab inventory's **Weapon parts** category with the hand-made receivers and gun
  parts, the builder's categories folded into eight sections (the search still finds "muzzle" or "splitter"), with their 3D tiles. They are not on the
  parts rack by default (a map's `rackParts` can still name them).
- **Creative-only.** The rules' `creativeOnly` categories (`upgrade` and `multiplier`, the Zombies-economy parts) are
  offered only with infinite parts: the parts menu refuses them on a map with `"infiniteParts": false` ("the X is
  creative-only (a Zombies-economy part)"). Question for the developer: should they ship in survival?
- **On construct guns.** Any builder part can be fitted to a construct receiver. Barrels, splitters, feeds, ammo,
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
- **Standard builds**: the Tab inventory's **Builds** tab spawns any of the 143 complete into the hands (creative only).

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
- **Coupling and uncoupling**: with the **attach tool** out, F on a hitch couples it to the nearest free hitch that fits (a
  ball's point within 0.6 m of an eye's; a hook's eye within its chain and 0.3 m) or uncouples it. Bare hands or another
  tool: "take out the attach tool (3) to couple or uncouple". A coupling also goes when either hitch is **shot off** or
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

### Ropes and springs

Free constraints between any two points, tied with the **attach tool** (the old attach tool; its code name is still `ConstructTool.Welder`, byte unchanged): its ring has **Rope** and **Spring** at the two ends, with Socket attach, the default, straight out from the tool. The attach tool joins pieces; the linking tool links what is already placed. Code: `Source/Constructs/ConstructConnectors.cs` (the list, the tick, the solver rows), `ConstructEditsConnectors.cs` (the server's checks), `Source/App/Program.SandboxConnectors.cs` (the tool), `Source/Render/ConnectorRender.cs`, `Source/UI/UiSandboxConnector.cs`.

- **Using it.** LMB picks the first point on any construct, wreck, loose body or the world (terrain, map geometry); walk if you like (up to 120 m); LMB picks the second. A ghost line runs to the crosshair, green, or red with the reason, and the HUD line gives its length, size and survival cost. RMB or Esc cancels. With nothing held, RMB at a line takes it off. R steps the size: by the parts at its ends, S, M, L, XL. The **configure tool** on a line opens its panel (length; strength or stiffness and damping); the **remover**'s LMB deletes one at once.
- **Rope.** Its ends stay within its length (new: the gap + 2% + 5 cm); below that it is slack and doesn't push. A one-sided row in the contact solve, the coupling chain's row, warm started, each side moving its body and every joint up its part's chain. Over its rating (smoothed: a quarter of each tick's pull, so one jerk counts but one tick's noise doesn't) it **snaps**: both halves whip back to their ends, with a crack, fibres and a word to the players near. Nothing else damages it. A rope has no weight, doesn't touch anything along its length, and isn't shot through.
- **Spring.** Pushes and pulls to a rest length (new: the gap) with a stiffness (N/m) and a damping (N s/m; new: 30% of critical for what it held when fitted). An implicit soft row, `ConstructFlex.FlexPrep`'s sum on two points, so no stiffness can blow it up. **Stiffness 0 is a plain damper** (it draws as two tubes): no kind of its own. A spring doesn't snap.
- **Sizes.** S, M, L, XL: rope 1.5, 6, 25, 120 t; spring 15, 60, 250, 1200 kN/m. By default the smaller end part's biggest side picks it (up to 0.6, 1.5, 4 m). The panel can set a rope weaker, or in creative up to 20 times stronger. The size itself is fixed once tied.
- **Cost.** Creative: free. Survival: from what you carry, then your beacon's network, no Build Station: **wood** for a rope, 1 unit a metre times 1, 2, 4, 8 by size, rounded up; **metal** for a spring, 2 a metre of its rest length, times the same. Short of it, the refusal says so as a look line (`Rope: 12 wood - you reach 5`). Lengthening a rope in the panel pays for the new metres. Nothing comes back when one is taken off: a rope is used up. A pasted one is free (a clip's cost is its parts).
- **Storage.** One list on the world (`ConstructWorld.Connectors`, 1024 at most, grown as used), whether its ends are on two bodies, one body across a joint, or a body and the world. It is never a segment. Ends name their parts by `CPart.Tag` with the point in the part's own frame, so a rope survives other parts coming off, splits, joins and moves. It goes when an end's part goes or is wrecked (a tie made to a part already wrecked stays), when it is pulled 6 m past its length, or when the ground under a world end goes (one end a tick is checked). Both points on one rigid piece is refused (a joint must lie between them). World saves: section `Connectors`. Blueprints and selector copies: `"connectors"`, when both ends are in the selection; world ends stay behind.
- **Wire.** `CBodyKind.Connectors`: the whole list (a joiner, after the bodies; a big change), or a patch of those made or changed (renumbered parts) and the ids gone; u16 part indices. `CActKind.Connector` new, delete, set. `CFireKind.ConnectorSnap` carries both ends. Clients draw from the list; the pull isn't sent, so taut or slack is worked out from the distance.
- **Waking.** A taut rope or any spring wakes a sleeping body when the other end moves (a body, or an end carried by a joint), and both ends are simulated together in a big world. A sleeping or anchored body is a fixed point to the row.

**Tool or parts.** Both exist. The tool gives the free constraints: any two points, any length, no sockets, no 40-odd part files by size and length, and it reaches the world; its costs are that it is not a thing you can place in a catalogue, attach, shoot or see the mass of. Dedicated parts (`feat/linear-joints`: sliders, electric and shaft actuators, rails, and Pistons) are for guided and powered motion, which needs a socket, an axis, a segment and a motor. A rope or spring gives none of those: it is a row between two bodies, so it can't guide anything along a line, and powered travel stays with the parts.

### Hydraulics

`Source/Constructs/ConstructHydraulics.cs`, the ram row in `ConstructConnectors.cs`, parts from `tools/constructs/hydraulics_parts.py` (`Constructs/Parts/Hydraulics`).

- **Cylinder.** The attach tool's fifth ring option. Like the Spring: click two points; R cycles the size (electric S to XL, then hydraulic S to XL). A `ConnectorKind.Cylinder` pinned on ball ends, with a capped two-way row in the contact solve, so it drives hinges as a real linkage (the rope's one-body path across a joint). Its A end stands on a tool-made **head** part (`toolOnly`: never in the inventory or the Assemblers), freeform-welded at the first point, which carries the ports: that gives it hoses, health and a place in blueprints, saves and joins with no new format. `Stiffness` holds its shortest length, `Damping` its longest, `Rating` its most force, `Length` what it is told to be, `RowForce` the force this tick (0: limp, no row). Cost: 4 times a rope's a metre, through `BulkPay`.
- **Electro-Hydraulic Cylinder.** The electric heads: self-contained, weak and slow. Power and a Set signal (0 to 1 of the stroke), nothing else.
- **Circuits.** No fluid simulation. A Hydraulic Pump, turned by a shaft or on power with none linked, charges a circuit of pipe links (decision: the pipe type with fluid oil, no new link type). `Solve` runs at 6 Hz (`Hz`) per construct: each pump has one pressure and one flow, each head traces pump, valve and hose (`Trace`). Force is pressure times area, capped by the head; speed is the head's share of the flow over its area, so ten cylinders on one pump go a tenth of the speed. A circuit stays on one construct: world links aren't traced. Nothing is held in the pipes: a shut valve holds what it holds with the pump stopped.
- **Cut hose.** The head goes `HoseCut`: `RowForce` 0, limp, and it sprays (a fire event). The wrench or attach tool repairs the hose as any link.
- **Engine load.** Work on the rods comes off the engine (`Construct.HydLoad`, in `ConstructDrillDrive.Carry` with the drills and rails), as displacement times the pressure the work asks; a head that can't move asks the full relief pressure. An engine held under 30% of that load for 2 s stalls, dead for 3 s (`StepStall`, and `DriveOn` is off meanwhile). A gearbox in the shaft trades speed for torque. A small pump doesn't stall the 260 N m Engine; a Medium pump against a stop stalls it and the V8.
- **Valves.** A valve's signal, -1 to 1, is how far it is open and which way. Lever Valve: the Analog Lever's handle, sprung to the middle, metered by how far you push; stack them side by side for a bank (each is one section). Digital Valve Block: a `scale` logic part driven by data, from a seat's Input Reader or any logic.
- **Cost of nothing.** A construct with no hydraulics has empty lists: `StepHydraulics` skips it, and `Solve` allocates nothing.

### Cable alignment

Three things keep links tidy.

- **Beside.** The auto-router's lattice has planes one link-width off every existing run (`RunBox`: the two radii and `LoomGap`), and a run there costs half (`BesideFactor`, against 0.8 along a surface), so a new link lies beside an old one when that isn't much further. Tray lanes get the same discount.
- **The Loom.** A mode on the linking tool's ring. LMB picks links on one construct (2 to 12; LMB again drops one), R goes on to the run, LMB lays its points (each square to the last; Free routing lets it go any way), R lays them; RMB steps back. `ConstructRoutes.LoomPlanFor` runs on the client for the preview and on the server for the edit (`CActKind.Loom`): the links lie across the run one link-width apart, in the order their ends come so they don't cross, every bend mitred so the spacing holds round it. Each link joins the run from its own end, straight when the path rule passes, otherwise square; a link whose ends sit the other way round runs the bundle backwards. All of them pass the one path rule, or none changes. A shaft can't be bundled, and a link with branches off it is refused. New segments are at full health, as with Re-route.
- **Cable Trays and Clamps** (`ConstructTrays.cs`, a part's `tray` block). A node set on one snaps to its nearest lane, the auto-router prefers lanes, and `ConstructRoutes.Damage` takes off up to 80% (`Shield`) by how much of the segment lies in a guard. A clamp is short but guards a metre of run. A construct with none costs nothing.

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

A joint's motion pushes back on the body it is mounted on: what the joint gained this tick (its speed against the last
tick's, in momentum: `_jVel0`, `_jReacted`), whatever gave it, the body takes back as a turn about the joint's axis or a
piston's push at its pivot. A turret swung hard rocks a light buggy; a turntable under a block hanging on a chain turns
the block the other way; a slider that hits its stop kicks the body. Steering pivots don't (they are the driver's hands
on a rack). `ConstructPhysics.JointReactions` turns it off (the self-test compares).

It is the motion, not the motor's push. A motor that only holds an arm up gains no motion, so it reacts nothing: that
weight is on the body already. Three details keep the books right for joints in series:

- The reaction goes through the whole body's inertia (`_invI`), because that is the body the joint's solve leaves
  unchanged. An arm heavier than the base it stands on stays stable; the base's inertia alone gave it momentum from nothing.
- A body's mass and centre are those of its rest pose, so `ArmWeight` adds the turn from each jointed piece's weight
  being somewhere else now, through what holds the body up last tick (`held`: ground, wheels, thrust; nothing in free fall).
- A body the physics gun holds takes neither: the beam carries the arm's weight and what swings on it, so the body isn't kicked against the beam's aim every tick.
- What a joint carries is posed joint by joint (`Construct.PoseCarried`, `SegNow`), so an elbow's bend moves the shoulder's load.
- What a joint turns has the inertia of its spread as well as its centre's swing (`SegSpin`, rest shape), so a beam centred on a
  turntable is as heavy to turn as it looks.

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
turn with the coupling points within 1 cm; bare hands refused, the attach tool uncouples (the buggy drives off alone) and
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
5. Coupling is **instant** with the attach tool (F), not timed; a rigid coupling made up to 0.6 m apart pulls together.
6. A body's mass, centre and inertia use the rest pose, with `ArmWeight` for the weight's moment; so an arm reached out tips
   the body truly, while the body still turns about its rest centre and with its rest inertia.
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
  on a plate stacks. Its edge sockets are 1 cm into the plate, so a plate edge attached to a beam's side sits on the
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
3; angle 4 kg and channel 6 kg, armour 6 and 7. Shapes are solid steel (armour 8). Armour class parts recycle at a higher rate than steel (the recycling table). Wood and glass are fitted with the attach tool, everything else with the attach tool;
heavy armour takes 5 s to attach.

**Shapes**: a part box may take one of the weapon builder's contoured shapes (`"shape"`: wedge, arc, cylinder, bevel,
rounded..., and on construct parts `hull`: `"points"` in the part's frame, drawn as their convex hull; `Source/Zombies/PartShapes.cs`, Parts/README.md "Shapes"), but only as a detail box (drawn only): collision
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
has a socket every 16 m, so 64 sockets a part are plenty). A socket snaps, autolinks, joins and socket-joins only to a
socket of its own class, and a mismatch says "use an adapter". The freeform and point attachments (one part anywhere on another)
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
| Joints + freeform attachments | 16 (8 joins) | 255 (64) | a segment is a byte everywhere |
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

**Choices made (the developer may change them)**: the physics gun lifts 20,000 t in creative with at least 8 m/s2 (it was 15 t), and survival stays at 500 kg, so a big part there goes on with the socket attach or a crane; four classes at x4 steps; strict socket classes with adapters, and the
freeform attachments as the escape; a stair is one cell at every class; plate thickness and kg/m2 by class; the game's own soft
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
module points (cells 0, 7, 8 and 15 of each side) where walls stand; a slab on the walls' tops joins to every one it
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
| Doors and gates | Wood and Steel Door 1.25 m, Garage Door Leaf, Fence Gate: door-role leaves hung on the existing **Hinge** (so Interact and logic open them; a hinge's base goes on a jamb's socket). The flush doors for these openings are in [Doors](#doors) |
| Defences | sandbag walls 2 m, 1 m, low 2 m, corner; Concrete Barrier 2 m; Blast Wall 2 m (3 m T-wall); wire-topped Wire Fences 2 and 4 m (seen and shot through, not walked through); Bunker Module (4 by 4 by 3 m, slits, a back doorway) and Pillbox (3 by 3 m), both foundations that anchor themselves |
| Watchtower | Tower Platform 2x2 m (parapet on three sides), Tower Brace, Tower Post 1.25 m, Tower Roof; the legs are Wood Posts |
| Columns and beams | concrete, steel and wood columns 2.75 m; beams 2 and 4 m |
| Damaged | the walls' damage stages (not offered) |

**Materials** (per square metre of wall; walls burn by their material, as before):

| Material | kg | Health | Armour | Class |
| --- | --- | --- | --- | --- |
| Wood | 30 | 45 | 2 (burns) | item |
| Corrugated metal | 18 | 55 | 4 | item |
| Concrete | 300 | 260 | 25 | structural |
| Steel plate | 160 | 320 | 35 | structural |
| Reinforced concrete | 360 | 420 | 45 | structural |

A part's mass may now be up to 50 t (a 4 m pad is about 14 t; was 5 t).

**Walls lose chunks.** The straight walls (2 and 4 m, every material) have two damage stages (`"damaged"` in the file):
below 60% of its health a wall turns into its **Damaged** stage (a 1 m chunk gone from a top corner), below 30% into
**Breached** (also a hole at the bottom, 1 m wide and 1.5 m tall: crouch through it), and at zero it breaks off as before
(`ConstructWorld.LoseChunk`). A stage has exactly the wall's sockets, so every attach stays; its health carries on from the
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
right tool for the part (decision 2: attach tool for frames, panels and foundations, attach tool for bolted parts) sets it down
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
- **The Construct Core** (`Constructs/Parts/ConstructCore.json`, `"core": true`): a 0.5 m orange attached block with a socket
  on each face, in the inventory's Structure group: the obvious starting point. A core is never a loose part (it can't be
  picked up with F; a single dropped Frame Block can) and is the root after seats and foundations.
- **Building onto a wreck or a loose part** (a dropped part, the map's loose parts) counts as building on that construct:
  the ghost sockets show on it, and the first part fitted **claims** it (no longer a wreck, the builder's own, so it no
  longer clears away as debris).
- **Joining two constructs**: with the attach tool out, aiming at a part whose free structural socket meets a free structural
  socket of another construct (face to face, within 3 cm, both on their main bodies rather than beyond a joint, the two
  bodies square to each other) shows "touches construct N - F to attach the two into one"; **F** joins them
  (`CActKind.Join`, `ConstructWorld.Merge` in `ConstructJoin.cs`). The other construct's parts move into this one's frame
  (each takes one more Rot24 turn, so nothing bends), with its joins, joints (their pivots and axes turned), wires, fuel
  and charge; every other socket pair the two meet at joins too; a coupling between them goes; a wire between them becomes
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
the limit is 12 constructs a player; joining is instant (the attachment, no timer) and on F with the attach tool; anchoring without a
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

**Half shafts.** A drive shaft from a differential output (or a gearbox, transfer or engine output) may end on a suspension's
**Drive in**: see [Half shafts](#half-shafts). The wheel on that suspension is then driven as if it were bolted on: the
torque split, ratio, losses and gates are the line's own, and a straight half shaft loses nothing.

**Forward** is `Construct.DriveFwd`, worked out once with the caches (`BuildRollSense`): the facing of the seat that controls it through a Driving Module (`Construct.ControlSeat`), else the first seat on the body (a seat
attached freeform counts), or the seat's right when the wheels roll across the seat, so it is never square to the wheels. Each
wheel's `RollSign` comes from it, and `Construct.RollAxis` is the axis its spin turns about, so a positive spin rolls it forward
however it was mounted: left or right, rolled on its socket, attached, mirrored. Physics, the drawn spin, tracks' rollers and a
tread's speed all use it. A wired or skid-steered drive still sends the sides opposite ways through `SideSign`, on purpose.

**Steering differentials** (kind `steering`: the Steering Differential, the Wide Steering Differential, and the Medium, Large and
Capital Steering Axle). A deliberate choice of skid steering, on wheels, Track parts or freeform tracks, bolted on or on half shafts.
Throttle goes to both sides evenly; steering adds to one side and takes from the other (`throttle - steer x SideSign`, clamped), so a
full steer at zero throttle counter-rotates the sides and the vehicle pivots on the spot, and the steering alone starts an engine that
needs a starter. Which side a wheel is on is measured from the differential, not the centre of mass.

- **Against an open differential:** no grip limit and no wheel in the air stealing the drive. Every touching wheel gets its share, like a
  locked axle (`Share`), but it doesn't scrub the turn, since turning is what it does. A side in the air spins free.
- **Braking:** the brakes win. Brake with steering stops both sides straight, and a neutral vehicle stays parked; with any steering
  held the parking hold lifts so it can pivot.
- **With steering pivots too:** both steer. The sides do the pivoting at a standstill and hand over to the pivots as it moves: the
  sides' share of the steering falls to 15% by 8 m/s (`PivotFade`). In reverse the pivots still steer as a car does.
- **Sliding:** while steering, tyres have 15% of their grip sideways (`TurnGripWheel`), free wheels 3% (`TurnGripFree`), and tracks
  45% as before, coming back with speed.
- **Automatic skid steering stays:** a construct whose driven wheels don't steer still skid-steers through any differential, so
  shipped crawlers keep working. The new part adds the even drive, its own sides, and skid steering alongside pivots.

The look line reads `Steering Differential: steers the sides - wheels on one side only: it can't turn`. It has no wired input: the
steering comes from the driver's seat (through its Driving Module).

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

### Wheels: the rim and the cylinder

`Source/Constructs/ConstructWheelShape.cs`. The spring stands on rays, so a wheel still carries weight, grips and steers as it
did; what is new is that it has a shape.

**The rim.** One ray at the axle says what is under the hub. Before this a wheel was that one ray: it saw a kerb only when
its axle was over it (the wheel went into the kerb, then snapped up), lost the ground over any gap under its axle, and a big wheel's
hub followed every bump. Now, when the rim matters, four more rays go down at 0.5 and 0.87 of the radius fore and aft of the
wheel's place next tick, and two at the tread's edges. A ray that finds ground `s` metres from the axle asks the hub
to ride `sqrt(r*r - s*s)` over it, and the highest ask wins, up to the spring's travel.

The rim matters (`RimWanted`) when something stands near the wheel (the cylinder's quick test saw it last tick), when the ground
falls away under the axle, and on terrain when the wheel is wider than a cell. On an open road or a deck a wheel still costs
one ray. A vehicle of more than 32 wheels asks 32 of them a tick, in turn, and holds the rest's last answer. So:

- A kerb lifts the wheel from where the rim first meets it, by as much as a circle would. Where the height jumps between
  two rays its edge is found by halves (3 more rays), so the rise is smooth, and a damper never sees more than 2.5 m/s
  from it (a tyre takes the rest of a blow).
- A step higher than 0.6 of the radius isn't climbed: its rays are ignored and the cylinder meets it, as a wall, its push level: the
  block's top edge can't throw the wheel up and over.
- A gap narrower than the wheel is bridged, the hub dipping by what the circle would. A thin edge under the tread carries it.
- A tread's edge rides up a low kerb met side-on (0.12 of the radius, a tyre's shoulder); higher, the side of the wheel stops.
- A big wheel on rough ground rides the tops of the bumps.
- A rail too thin for the rays still lifts the wheel: the cylinder's point asks the spring for that height next tick (`RimAsk`).
- On flat or even ground the rim's rays don't run, or the axle's wins, so a car on a road does exactly what it did.

A client draws its wheels with the same rays (on vehicles of up to 32 wheels; more, the axle's only).

**The cylinder.** A wheel on a suspension has one extra leaf in the [compound collider](#compound-collision), at the hub as drawn:
its travel, its steering, an arm's arc and lean ([Suspension types](#suspension-types)). The leaf's bounds hold its whole travel,
so its tree is never rebuilt as the hub moves; only the narrow test reads where the hub is now. It goes through the hull test
as a **sixteen-sided prism** (32 corners, the hull limit; 18 faces, within 1% of the true radius, at any size), sized from the
wheel's radius and width. The wheel's boxes stay in the tree for the physics gun's step and ray hits, and collide no more.

- Walls, kerbs too high to climb, other vehicles, loose bodies and steep ground stop the wheel. The knock goes to the body at the
  contact, not through a steering pivot.
- What the rays carry isn't met twice: a point pushing the wheel up by at least 0.4 (the steepest step it climbs) is dropped while the spring
  has travel left, and one pushing it straight up (0.85) even when bottomed, where the rays' hard stop has it. A body too
  light to count as a deck (a crate) meets the whole cylinder.
- A free wheel's contact with what can stop it has low friction (0.15, it rolls). Against a crate or on an anchored
  base the tread drives what it touches (`TreadVel`, from the hub now). Friction against the tread's speed on a wall ran cars up the
  wall and over.
- A piece that is further than the cylinder's sphere, or wholly under a wheel the rays carry (a floor, a deck's plates), never reaches
  the hull test, so standing on a deck adds a few comparisons.

Not cylinders: tracks (one ray per contact point, their boxes as before), a wheel with no suspension, and a big construct's wheels
against the ground's triangles when the construct asks for the ground leaf by leaf (`GroundLeafByLeaf`: over 900 cells). Players
and bullets still meet the wheel's boxes at their place at rest.

**Any order.** A wheel is sprung when its axle socket is linked to a working suspension's and its axle lies level, whichever came first: wheel on the suspension on the vehicle, or suspension on the wheel and the piece onto the vehicle, by the hands, socket attach, point attach or freeform, on the grid or off it. A piece with wheels may go on off the grid; on a suspension welded on at an angle a wheel still travels straight up and down in the body's frame (`CWheel.Up`), and an arm's arc is left out, so it moves in a line. "Wheel orders" drives them.

**The tyre's settings.** Every wheel, track and tyre gets them from its role (`ConstructConfig.AddWheelSettings`), each starting
at its part file's number, so nothing changes until someone moves one; a part file only lists one to change its range or text.
They live on the part like all settings: blueprints, world saves and the construct message carry them.

| Setting | Key | Default | Range | Does |
| --- | --- | --- | --- | --- |
| Dry grip (g) | `friction` | `grip` times the material's `friction` | 0.05 to 4 | The friction circle on hard ground; its share of the default also scales the cylinder's contact friction (at most 2x) |
| Dirt grip (x) | `dirtGrip` | `offroad` | 0.1 to 3 | Times the dry grip on terrain |
| Side grip (x) | `sideGrip` | `side` | 0.01 to 2 | Times the sideways grip |
| Rolling drag (%) | `rolling` | `roll` | 0 to 50 | Share of the load |
| Bounciness (%) | `bounciness` | 0 | 0 to 100 | Less damper (at 100%: 10% on the way back up, 40% going in); the hard stop and a cylinder's knock give back 70% of the closing speed |

A flat tyre's half grip and extra drag come on top of these. "Tyre pressure" isn't a setting: it would only be dirt grip and
rolling drag moved together.

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

**Boxes that move** (a detail box's `"rig"`; `"lever"` is the [Analog Lever](#the-analog-lever)'s handle): `"hub"` goes with the wheel; `"arm"`, with its `"pivot"`, turns by the arm's angle;
`"leg"`, with `"from"` (fixed) and `"to"` (moving, at rest), is drawn stretched between the two: `"pivot"` turns its moving end,
without one it moves with the hub, and `"span": [a, b]` draws that share of the way (a sleeve, a coil's ring). A leg's file box
gives its width, and stands straight at rest so a ghost and an icon look right. The solid boxes don't move: each part is a
hidden solid at rest, what collides and what the sockets sit on.

**Driving.** A suspension is driven by its link to a differential, a hub or a fed steering pivot's leaf, or by a [half shaft](#half-shafts) into its Drive in, whatever its kind, and refusals read as before ("a steered wheel needs a U joint or CV joint to be driven"). A
Pogo Strut goes on a differential's end or a pivot's leaf by `inner`. On a steering pivot the whole arm turns with the steering,
so a swing arm's wheel swings round the pivot on the arm's length: keep steered arms short. A Trailing Arm moves its wheel a cell or
more back along the car, which shifts the load towards the other axle: the Trailing Arm Buggy pulls away a little slower.

**Blueprints**: Pogo Buggy, Swing Arm Buggy, Wishbone Buggy and Trailing Arm Buggy are the Buggy on each kind, generated from it.

### Half shafts

Code: `Source/Constructs/ConstructHalfShaft.cs`, the drive solve in `ConstructDrive.cs`, the end drawn by `RouteRender`. A shaft link
that ends on a suspension's **Drive in** drives the wheel on that suspension. Every suspension that takes a wheel has one (plain,
Pogo Strut, Swing Arm, Double Wishbone, Trailing Arm, Leaf Spring, Rigid Mount and the adjustable ones, every size and the
Medium, Large and Capital classes; not hub motors, which need no shafts, or landing struts). The port is on the hub's inboard side:
at the hub's height on a bracket and a Pogo Strut, below the arm on a Swing Arm or Double Wishbone, so the shaft runs under the arm.
It stands on a socket of kind `port`, on which nothing bolts.

**Linking.** Start a shaft at an output as usual, and aim at the suspension or at its wheel: the linking tool ends at Drive in, with
the auto-route ghost, the picker, Free routing and hold to adjust all as for any input. The ghost and the port's marker sit where
the hub is now.

**The moving end.** The nodes stay on the output's side and the link's rules are checked at rest (hub at mid-travel, joints at 0).
Only the last point moves: `ConstructRoutes.LivePoint` follows the hub (`Construct.HubPoint`), so the last segment is a CV joint at
the hub and a plunge joint along its length, drawn with a sleeve and a boot each frame, and shots hit it where it is drawn. It
isn't refused as a shaft crossing a joint. Across a Steering Pivot it works too, with the suspension on the leaf (a joint-angle gate
of 45 degrees, a CV joint's). Any other joint is refused.

**Refusals**: "too far: half shaft reaches 4 m" (`HalfShaftReach`: the whole link, 4 x the part's longest side, at least 4 m; a half shaft's length comes from its arm, so this is the one link with a limit on its whole length, and it replaces the 8 m a segment, since a capital arm alone is 32 m),
"a half shaft goes into its hub at most 30 degrees off its line (38 here)", "at the end of its travel the half shaft is 34 degrees off
the hub (at most 30): come in straighter, or from further away", "a half shaft crosses a suspension's travel or a steering pivot, no
other joint: fit a U joint or CV joint there". The arm's mount counts as a part in the way: the clipping grace forgives the part
only at the hub (plus, on a bracket whose Drive in sits on its mounting face, what it is bolted to, since the shaft has to come through it).

**Driving.** A half shaft from a differential is part of that differential's group, so open, limited-slip, locked and lockable
behave as with bolted wheels. From a gearbox or transfer part it is a locked group of its own. From a power take-off it does nothing
(a look line says so). A wheel with no half-shaft line behaves as before. With no wheel on it the suspension's look line says "no
wheel on it: the half shaft ends here"; a shaft that drives nothing says why on its own look line.

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
  90 degrees; a freeform attachment can set any angle.
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

`ConstructEngine.Build` runs with the caches. Working pieces that touch (socket links, a join, a freeform attachment's bead) with the
same `MoveSeg` are one engine: a turntable between them breaks it, a freeform attachment doesn't. Bank directions are compared in
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

## Tracks

Two kinds, one contact model. A **Track part** (Track, Short Track, Bogies and the Medium to Capital tracks) is a whole
track in one part on one suspension. A **freeform track** is a loop the player lays round wheels with the **track tool**,
like a belt on pulleys: triangle tracks, long lows, a raised sprocket, any wheel size. Code: `Source/Constructs/ConstructTracks.cs`
(rules, store, contact rays, tick), `ConstructEditsTracks.cs` (server), `Program.SandboxTracks.cs` (the tool),
`Source/Render/ConstructRenderTracks.cs`, `UiSandboxTrack.cs`.

### The track tool

On the tool wheel (the tenth tool, between the physics gun and the attach tool). LMB picks wheels one after another round the loop;
LMB on the first wheel or R closes it; RMB steps back; LMB on a picked wheel leaves it out; Delete drops the stroke. A green or
red band shows the track the picks make, and the look line says why not when it is red. The order doesn't change the shape: the
band pulls tight round the wheels' convex hull, so a wheel inside the hull is carried along but doesn't touch it.

With the tool out, on a track: R opens its settings (so does the configure tool's LMB on one of its wheels), Delete takes it off,
and LMB puts one that was shot off back on. Settings: **width** (starts across its wheels), **link length** (a share of the
smallest wheel), **tension** (how much the top runs sag: drawn only), **friction** (its grip, 1.05 like a Track part).

Rules (`ConstructTracks.Check`, the same on the client and the server): 2 to 48 wheels, all wheels and no Track part, none on
another track, all on one construct, stood up, turning the same way, in line within a wheel's width, on the body or all on
one steering pivot (no moving joints, no freeform attachments), at most 1,600 m round, 64 tracks a construct.

### Stored as

A track is **not** a routed link: `Construct.Tracks` (`CTrack`) beside `Construct.Routes`, holding its wheels (`TrackParts`), four
settings and its health. Links are point-to-point between ports, with a type's rules, a health per segment, spans of at most 8 m and
every system reading them by type; a track is a set of wheels. It goes through the same places routes do: `Remap` (parts
renumbered, a piece split off: a track goes with the piece only if all its wheels do), `Merge` (joins and joins), the full form
(so the patch form too: protocol 136), a blueprint's `"tracks"` (`{"wheels": [steps], "width", "link", "tension", "grip"}`, the
selector's copies and saved blueprints), and the end of a body in a world save (a body's bytes carry a length, so older saves
just end before it). A copy's tracks are whole. A wheel taken off leaves its loop; with one left, the track goes.

### The contact model

No link is a body. A track is a handful of contact rays, one speed and one drive (`ConstructPhysics.TracksDrive`, `TracksShare`).

- **The rays** are `CWheel`s, so springs, slew, grip, skid steering, decks and hub motors are the wheels' own code. Each wheel of a
  loop is one. Between them `BuildStations` adds rays along the loop's lower edge, by the run's length (spaced at least 0.6 wheel
  radii, at most 13), each on the nearest wheel's suspension. A wheel on no suspension still works: it is a raycast wheel on
  the track, with a little give of the track's own (`NoSpringHz`).
- **Lead rays** (`CWheel.Lead`) lie on the rising stretches: the curve round an end wheel, a raised front run, a raised sprocket.
  They touch nothing on the flat, carry no share of the weight (`Construct.LeadCount`) and hold no ride height, and a ray straight
  down from high over the band's edge sees a kerb before the body reaches it, so the nose rides up. A Track part gets four (two
  each end, `LeadRays`). What climbs is what the band's curve reaches; a step over the axle meets the body's solid parts.
- **One speed.** All of a loop's wheels share it: what the contacts make of the ground, or in the air a driven wheel's.
- **One drive.** Every driven wheel of a loop adds to the track's drive, touching or not, and the contacts share it by their grip,
  so a sprocket held off the ground drives the whole track. A raised sprocket counts as touching for the drive line.
- **A Track part is one wheel to the drive line.** While any of its rays touches, all count as touching, and those on the ground pass
  on the share of those that aren't (`TrackUnitsShare`), so one ray over a dip no longer reads as a wheel in the air on an open
  differential (which passes a tenth of the torque).
- **Skid turns.** While a track skid steers, its pads have `TurnGrip` (45%) of their grip sideways, so long tracks can swing (it comes back as the speed rises: an arc at speed, not a spin), and
  turning on the move the inner track is held back by `TurnBrake`, so the nose comes round instead of the outer side only
  speeding up. Hub-motor bodies keep `SkidSlack` as well.
- **Hills.** No collider: the wheel system changes once a wheel is on a track. On a grade a track's pads bite (`GradeGrip`: `BiteGrip`
  50% more grip at 45 degrees), it pulls low (`ClimbBoost`: up to twice the drive up a grade, fading out by `ClimbSpeed`, 6 m/s) and
  under throttle it doesn't slide back while the pads hold. Wheels get none of this. Dirt, a curved run-up, 20 s on full throttle: a
  Tracked Crawler holds 50 degrees (30 before), a 4x4 Truck 35, a Buggy under 30.
- **Climbing.** A wheel's ray past its travel only stops the body sinking; a track's lifts it (`ClimbRate`, up to half its slew), so a nose
  that meets a step higher than its spring goes up it instead of through it.
- **The band has a thickness.** A wheel of a track, and a Track part's rays, ride on the band: the ray reaches `CWheel.Band` (the link's
  thickness) past the radius, so the links lie on the ground and the wheels sit on them.
- **Parking.** With the throttle released a wheel stops dead under 1.5 m/s; a track runs down at `ParkDecel` (plus what a slope adds) and
  parks under `ParkSpeed` (0.25 m/s). Under throttle nothing holds it.
- **Slew.** Every track contact follows a step at `ConstructTracks.Slew` (2.4 m/s; a big suspension's `slew` wins). The contact climbs a
  kerb instead of taking it in one tick, which is what stops the hop on rough ground.
- **Drawing the ride.** A Track part lies on its rays (`UnitPose`: a line fitted through them, up its travel and tilted). A loop
  follows its wheels: suspensions spring and the band wraps where they are.

### Drawing

`ConstructRender.DrawTracks`, one instanced batch after the bodies: a plain band of chords round the loop, and links (boxes) sliding
round it at the track's speed (`CWheel.Spin` times the radius: each side its own, so they run opposite ways in a skid turn; a
client works it out from the body's motion at the wheel). A top run sags by its tension. Past about 700 link lengths from the
camera only the band is drawn, and past the body's far level nothing. At most 900 links a track. A Track part is drawn the same way
(its two ends are its loop), with its `"roll"` boxes as before, now turned by its own side's distance.

### Damage

A track has health (`FullHealth`: by width and length). Rounds hit its band (outer face and sides, `ConstructTracks.Ray`) and blasts
reach it; at zero it comes off: it is no longer drawn, its wheels run on as plain wheels (a wheel on no suspension is a solid part
again) and only those a drive line reaches are driven. The track tool puts it back on. No debris falls yet.

### Cost

Creative: free. Survival: **metal** from what you carry, then your beacon's network, the path ropes use: 1 unit a link (`ConstructTracks.LinkCount`:
the loop's length over its link length, rounded up). Making one pays for the whole loop, its Mirror Table twin too; mending pays for the share of the links
its lost health stands for, so all of them for one shot off; setting shorter links pays for the extra ones. Nothing comes back when one is taken off. A pasted
one is free (a clip's cost is its parts). The look line shows the cost; short of it the refusal says so (`Track: 34 metal - you reach 12`).

### Not done

- A track has no collider: players and other bodies pass through the band (the wheels are raycast wheels too).
- The drawn band answers the ground, not the other way round: near the camera, rays along a lower run (`TrackLift`, up to 33 a track, a
  frame) lift it over the ground, rocks and other constructs and let it droop a little into a dip. It is a look: nothing physical.
- Tension is a look only: a slack track doesn't throw.
- A track's paint: none yet.

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
- **Tracks and Bogies**: wheels with a `track` block (long Tracks 4 to 6 contact rays, short Bogies 2 or 3, each with four lead
  rays round its ends: [Tracks](#tracks)). Their boxes with `"roll": true` are the exposed drive wheels and rollers, which turn
  on the spot as the construct runs.
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

## Linear joints

A rail and a carriage: what hangs on the carriage slides along the rail. Data: `Constructs/Parts/Linear` (written by
`tools/constructs/linear_parts.py`, 42 parts; `--check` compares). Code: `Source/Constructs/ConstructLinear.cs`. Inventory: Joints and
Motion, section Linear; size is the window's filter. Existing Pistons stay as they are.

| Kind | Joint | Runs on | Input | Unwired |
| --- | --- | --- | --- | --- |
| **Free Slider** | `slider`: no motor, rolls under load between its stops | Nothing | `Lock`: on, it holds | Rolls; F says it has no motor |
| **Linear Actuator** | `piston` motor and [strength model](#the-strength-model-constructphysicsmotorprep), draws power by load | The circuit (`Power in`) | `Set`: 0..1 along the rail (Position), or -1..1 of its speed (Speed); `Hold`: on, it stops where it is | Sits at its Rest position; F sends it to the far end and back |
| **Shaft Linear Drive** | The same motor, through a lead screw | A drive shaft into `Drive in` | `Set`: -1..1 clutch and direction, 0 holds | Runs while the shaft turns; F reverses it |

Sizes (travel is the section's length; the rating is the Force setting's top, a tenth of it the bottom):

| Class | Gauge | Travels (m) | Rated force | Top speed | Lead (m a turn) |
| --- | --- | --- | --- | --- | --- |
| Small | 1x1 | 0.5, 1, 2 | 6 kN | 2 m/s | 0.05 |
| Small | 2x2 | 1, 2, 4 | 24 kN | 2 m/s | 0.05 |
| Small | 3x3 | 2, 4 | 60 kN | 2 m/s | 0.05 |
| Medium | 1 m | 4, 8 | 300 kN | 2 m/s | 0.2 |
| Large | 4 m | 16, 32 | 5 MN | 3 m/s | 0.8 |
| Capital | 16 m | 32, 64 | 200 MN | 4 m/s | 3.2 |

A driven rail takes 5 times its rating across its axis (a piston a third of it): it is made to carry what rides on it, so a
flat rail holds the load off the axis. A Free Slider has no rating: it holds any load, across or at its stops.

### The joint

A section's axis is its length, and the carriage rests at the section's middle, on the leaf socket `top`. The joint's number is
the carriage's distance from there, so unlike a piston's it runs below 0 as well as above. Hanging parts on the carriage works
as on any joint (`ConstructBuild.OnLeaf`, `LeafRefusal`, `HangOnLeaf`; attach tool, socket attach and freeform attachment alike). The carriage
is drawn, not solid: what hangs on it collides. Riders and vehicles ride what is on it (`ConstructDeck`, contraption space), and
the Load bar counts them.

### Stacking

Sections joined end to end by their `end-` and `end+` sockets make one rail: three 2 m sections are 6 m of travel, with one
carriage and one joint, and nothing chained. `Construct.BuildRails` works it out on every rebuild, on every peer, before anything reads
a joint's limits; none of it is saved or sent (the wire and the save carry ordinary parts, links and the joint's number):

- **What joins.** Same gauge and size class, on the same moving piece, in line within 2 cm and 3 degrees, either way round; travels may
  differ. A freeform join's links count. At most 64 sections (`MaxRailSections`).
- **One carriage.** The joint is the section whose carriage carries something (`CJoint.Hinge`); the run's limits go on it, in its own
  axis, so a load on a middle section runs both ways. Other sections hide their carriages, and a second load on the same rail is
  refused ("it has one carriage"). Two rails already carrying loads, pushed together, stay two rails, split at the second one.
- **Mixed stacks.** A powered section anywhere in a run drives the lot: `CJoint.Drive` is that section, and its wires, power, shaft and
  settings are the joint's, whichever section's carriage carries the load. Free Slider rails are the cheap way to lengthen an actuator.
  The first powered section along the run wins; a Lock wired to a Free Slider in a powered run does nothing.
- **Settings.** The configure tool, on any section of a run, opens the section that runs it (`ConstructLinear.Master`).
- **Taking it apart.** A section wrecked or taken off shortens the rail, and a wrecked one splits it; each piece is a rail of its own. A
  carriage left out past the new end runs back at 2 m/s rather than jumping. The look line says "a wrecked section cuts this rail short".
- **Cost.** A run is one joint out of `MaxJoints`, one slot in the solve, and nothing per tick beyond that; the walk runs only on a rebuild
  of a construct that has rail parts.

### Motion

- **End stops** are the joint's limits, solved as rigid rows with the contacts. `stopLow` and `stopHigh` (configure tool, % along the whole
  rail, a stack too) move them in.
- **Free Slider**: gravity along the rail, what pushes on the parts beyond it, and `SliderDrag` (0.8 a second). Like every joint it is reduced
  coordinates: it feels gravity and contacts, not its base's acceleration, so a carriage doesn't run back as a car brakes.
- **Linear Actuator**: `rest` (where it sits unwired, 50% by default, so a single section doesn't move when powered), `setMode`, `speed`, `strength`.
  In Speed mode a Set of 0 holds; at a stop it stops pushing. Power is the motor's force times speed, like a piston's.
- **Shaft Linear Drive**: the speed is shaft rpm / 60 x lead (`lead`, in the configure tool), capped at the part's top speed. The force is the
  lower of its rating and 2 pi x engine torque x gain / lead, gain the line's reduction and losses and the screw's 90%: a shorter lead pushes harder and goes slower, as
  a lower gear does. It uses the drills' feed (`ConstructDrillDrive.BuildShaftFeeds`, `Linked`): a drive shaft to `Drive in`, or a
  drive part on that socket, through a gearbox, a clutch or a power take-off, or straight from the engine. Moving, it takes its force back from the engine
  (`Construct.RailLoad`, N m at the crank): less for the wheels, more fuel, and an engine that can't carry it is pulled down and it slows (`ShaftShare`).
  Reverse gear or a reverse throttle reverses it. A lead screw doesn't back-drive: with the engine off, the shaft cut or the clutch out it **holds**, up to its rating, and the look
  line says why. Its **Back-drive** setting (configure tool, off by default) gives it a steep screw instead: whenever the shaft isn't turning it, or `Set` is 0, it lets go like a
  rail with no power (`ConstructLinear.LetsGo`), with a little drag, so a load pushes the carriage back and the look line says "free" rather than "held". A running shaft still drives it at its rating.

### Network and saves

Protocol 135: joint type byte 7 (`Slider`), and a sliding joint that reaches past 30 m sends its pose in centimetres, not millimetres (a pose is
a short: millimetres stop at 32.7 m, which also held a 64 m Capital Piston short on clients). World saves store the joint and its limits as before.

### Decisions made (the developer may change them)

- A section's travel is its whole length, so the carriage overhangs a rail's end by half its length at the stops.
- Stacking doesn't add force: a run has one motor. Parallel rails on one load do not share it either (no closed loops).
- The numbers in the table, the side load (5 times the rating), `SliderDrag`, the screw's 90%, and the lead's range (a fifth to 4 times the default).
- A shaft rail is a self-locking screw unless its Back-drive setting is on. Back-driven, it has only the drag of unpowered gears and no static hold, and the physics gun still can't move it.
- The Set input of a Shaft Linear Drive is a clutch and direction; position by shaft isn't offered.
- Lock on a Free Slider is on or off, not a graded brake.

## Pieces of a mechanism

A construct with joints is a set of **pieces**: the base, and each segment a joint moves. Code: `Source/Constructs/ConstructFreeChain.cs`, `ConstructEdits.FreezePiece`. Protocol 139.

**Freezing a piece** (the physics gun):

| Press | What it does |
| --- | --- |
| Aim (RMB), holding a whole construct | Freezes every piece of it |
| Aim, holding one piece | Freezes that piece and lets go |
| Aim at a piece, holding nothing | Freezes or unfreezes that piece alone. The look line says which |
| Fire on a frozen piece | Unfreezes that piece alone and takes it |
| Fire on the rail or base of a frozen construct | Unfreezes all of it and takes it, as before |
| Reload twice | Unfreezes everything you froze |

So the way to debug a contraption: hold it and freeze it, then unfreeze the pieces you want moving, one at a time. Outlines and the frost tint show frozen pieces only.

- **Frozen means it stays where it is in the world**, whatever the others do. On something fixed (a frozen piece, a frozen or anchored base) the joint is locked (`CJoint.FrozenLock`): it costs no solve. On something free it is held by rows at three points (`ConstructPhysics.PinRows`, nine rows a piece), so with the base unfrozen and a pendulum frozen, the base swings and slides round the pendulum.
- A motor on a frozen piece lets go and picks up from where it stands when unfrozen. A flexible joint's ball freezes as one.
- One owner a construct: `Construct.FrozenBy` is who last froze a piece of it, and that is whose double Reload frees it.
- **Saves**: a joint flag; the pin is taken again from the saved pose. **Joiners**: a full body's joint flags. **Blueprints**: `"frozenBase": true` and `"frozenJoints": [steps]`.
- Frozen constructs saved before this keep their joints free: only the base is frozen. So does a survival rig. Those joints freeze with Aim like any others.

**Joints pass motion on** (`ConstructPhysics.CoupleJoints`). Each joint is one number, solved alone, so a slider's stop used to mean nothing to the pendulum on it. Now each pair in a chain with a free joint in it shares momentum, from the mass they share: the mass matrix's off-diagonal term at the centre of what the outer joint carries. What changes of that each tick goes to the free joints of the pair, before the solve, 85% of it (`CoupleShare`: one number solved a tick behind the other rings otherwise). A slider that hits its stop kicks the pendulum out; a swinging pendulum works the slider. Free joints take it, motors don't: a motor's arm stays rigid, though it swings a free joint on it. Pairs are found on rebuild (at most 128 a construct), and a construct without a free joint in a chain has none. A free body's own acceleration isn't passed to its joints.

## Free rotation

Joints that turn with no motor, spring or power. Data: `Constructs/Parts/Free` (written by `tools/constructs/free_parts.py`, 12 parts; `--check` compares). Code: `Source/Constructs/ConstructLinear.cs` (`JointType.Swivel`). Inventory: Joints and Motion, section Free rotation.

What was there before: a **Hinge** swings, but it springs to its latch and its leaf takes doors; **turntables, motor hinges and pistons** have motors and hold still without a signal; **flexible joints** spring back. Nothing turned freely.

| Part | Turns | Sizes |
| --- | --- | --- |
| **Free Swivel** | About the line through its leaf socket: a lazy Susan, a free turret ring | 1x1, 2x2, 3x3, Medium, Large, Capital |
| **Free Pivot** | About an axis across its leaf socket: a pendulum, a swinging arm | The same |

- Gravity on what hangs on it, what pushes on the parts beyond it, and a little drag (`ConstructLinear.SliderDrag`, 0.8 a second, as a Free Slider). It feels gravity and contacts, not its base's acceleration.
- A `Lock` signal holds it. **Limits** are a setting, off to begin with: `limited`, `min`, `max` (degrees). Off, it turns forever.
- The moving side (`"side": "leaf"` boxes) turns with what hangs on it, and the physics gun turns it by that side ([The physics gun](#the-physics-gun)).
- A Free Pivot with its leaf up falls over until something stops it: hang it leaf down for a pendulum, or set limits.
- Protocol 137: joint type byte 8.

Decisions made (the developer may change them): the names; the same drag as a Free Slider, so a spun Free Swivel stops in a few seconds; no rating (it holds any load, across or along its axis).

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

**The winch** (`"hitch": {"kind": "hook", "chain": 32, "winch": {"min", "speed", "rating"}}`): F with the attach tool hooks an
eye in reach, the chain starting as long as the gap; a signal of 1 reels in, -1 pays out; it stalls on a body heavier than
its rating. Protocol 118: a coupling's length is a u16 of centimetres; a reeling chain is sent every 25 cm.

**The fire director.** Wire a seat into it (else the seat that drives) and it into any number of guns. A [Turret Module](#control-modules) does the same for one turret and holds no fire; the director is for a ship's broadside. Those guns aim where that
seat's sitter looks (`Aim`, unwired joints only), hold fire until they bear within 8 degrees, and fire on the sitter's fire
key (the director's output is the trigger, one tick late like every wire).

**The helm.** `"seat": {"helm": true}`: when two seats each reach the drivetrain through a Driving Module, the helm drives. A helm with no module controls nothing, like any seat.

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

- A chute segment whose two ends are on different moving segments (`Construct.MoveSeg`, so a freeform attachment doesn't count) is a
  **flexible section** by itself: no new part, nothing to pick. It is drawn as a ribbed bellows curving between its ends
  (`RouteRender.Bellows`), and is free of the duct's 45 degree steps and of clipping, since where it runs at rest means
  little. It carries bulk and parts as any chute does.
- **Its limit**: it stretches to 3 times its rest length (at least 0.5 m more), folds to 0.3 of it, and its ends turn 135
  degrees apart. The bellows goes from the duct's colour to red over the last 30%, and the look line reads "flexible section
  1.4 of 3.0 m, bent 40 of 135 deg". **Past it the chute stops** ("STOPPED, stretched too far (bring the joint back)") and
  carries on when the joint comes back. **Past 1.25 times its limit it tears**: the segment is cut as if shot, a hint says
  so, and the attach tool mends it. A branch stops with the link it comes off.
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
| Drive shaft | round steel, a U joint at each bend | straight, at most 45 degrees a bend (and into a port), no corners (a gearbox does that), no bare junctions (a T gearbox does that), never across a joint (a U or CV joint device does that), except its end into a suspension's Drive in ([half shafts](#half-shafts)) | torque from engines, gearboxes, transfer parts and differentials, port to port, and into a Drill Module's Drive in | 250 |
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
when a rule is broken (too close, a node sinking into a part, a segment through a solid part, the bend, a span over
8 m between nodes). A link has no limit on its length: only each span has one (user, 2026-11-04). What is left is the
storage for nodes, 8,192 a construct (`MaxRouteNodes`, 65 km of link at full spans), and 8,192 in the world for world links. Fire on an input finishes it, by the route the game found ([Auto-routing](#routed-links-decision-20), below; a picker when it has several free ports, with the port nearest the crosshair highlighted so one more click confirms); Aim or Backspace takes
the last node off; Esc cancels. A straight data cable may still go into **another** construct's input (milestone 3's wire
between constructs). The server checks the whole link again (`ConstructRoutes.Check`). The player must be in reach of the input, the end clicked last,
and no longer of the output: a long link is laid by walking along it, placing nodes (through the Build Gantry's camera, its range counts as reach, for
laying a link and for the edit modes). The preview checks only the last segments as the crosshair moves (`PathRule`'s `checkFrom`), since the rest was
checked as it was laid; the server's check of the whole link runs once. Nothing in a link is read per tick by its length, and `ConstructRoutes.Ray` skips
16 segments at a time on one test when they are too far from the ray to touch it (`CRoute.RayRun`).

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
- **Nodes** are ordinary nodes: each goes on the nearest part on its side of the joint, in the path's frame (`Hosts`). Pieces freeform-attached at an
  angle count as their bounds. The client checks the result with `ConstructRoutes.Check`, and the server checks it as it checks a manual
  link: nothing new is trusted and nothing new is sent. It searches at most 24 m at a time (`RouteSearch.MaxReach`): further than that the look line asks
  for a node on the way, and it carries on from there.
- **Cost.** It runs on the client, 2 ms a frame (`BudgetMs`), and nothing allocates after its first use makes its tables (about 19 MB, once). A search restarts
  when the construct is rebuilt (`RebuildStamp` is in the key), and 8 results are kept per link and goal, so sweeping between ports searches nothing twice.
  Caps: 1,048,576 lattice points, 262,144 states, 200,000 states searched per stage. Waypoints wait for the crosshair
  to rest for 150 ms. Measured on the 2-core box under load, on random 30% full block hulls (shared ports, searches up to 24 m):
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
power cable sparks. The the attach tool (Fire, nothing in hand) mends a link; the edit ring's re-route starts it
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
water; none: any) and any port may say `"noEffect": true` (listed in the picker as "(no effect yet)": the exhaust and coolant ports,
a battery's Power in; the simulation has no exhaust or cooling system yet and does nothing with them, and a
tank's pipe into an engine's Coolant in does not feed it). A fuel tank's Fill takes fuel from a Fluid Tank ([Fluid tanks](#fluid-tanks)). The first port of a type on a part is port byte 255, the next ones
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

### Fluid tanks

Six sizes in `Constructs/Parts/FluidTanks` (written by `tools/constructs/fluid_tank_parts.py`, the fuel tanks' bodies and
capacities): Small Fluid Tank (15 l), Fluid Tank (40 l), Large Fluid Tank (150 l), and Medium, Large and Capital Fluid Bunker
(20 kl, 1 Ml, 4 Ml). Role `fluidtank`, code in `Source/Constructs/ConstructFluid.cs`.

**Fluids.** The game's list is `enum Fluid`: fuel, water, oil, steam, coolant, exhaust. Fuel is the only one anything uses:
engines, power plants and thrusters burn it. Everything else is carried and stored, and no part makes or consumes it yet (the
exhaust and coolant ports stay "(no effect yet)"). Refining and assembling work in bulk materials over chutes, not fluids.

**Two stores.** Fuel tanks share one pool per construct (`Construct.Fuel`), and pipes between them only say what reaches what.
A Fluid Tank is a store of its own: one fluid and a fill level, as two part settings (`fluid`, `fill`), so it moves with the
part and rides in blueprints, world saves and joiners' CBody like any setting. The two are joined at their pipes:

- **Direction.** A pipe runs from an output to an input as it was laid: each tank has a **Fluid out** and a **Fluid in**, and
  a pipe moves fluid only that way. Two tanks that should trade both ways take two pipes.
- **Flow.** Each tick a pipe with a Fluid Tank at either end moves what fits, up to the narrower tank's `flow` (a pool's end
  doesn't limit it) times its share at junctions, in tank to tank, pool to tank (a fuel tank's Fuel out) and tank to pool (a
  fuel tank's Fill). A cut pipe pours a Fluid Tank's contents out at 0.5 l/s, whatever they are.
- **Empty.** A tank with no fluid or at 0% is empty, takes whatever arrives first, and goes back to no fluid when it runs dry.
- **Mismatch.** A pipe bringing another fluid than the tank holds moves nothing and takes nothing out of the source. The link
  stays. The look line says so at both ends (`Large Fluid Tank: 15 of 150 l oil - refused: a pipe brings water, this holds oil`,
  `Fluid Tank: 20 of 40 l water - refused: the Large Fluid Tank holds oil`) and on the pipe (`WARNING: water refused, the tank
  holds oil`). Water piped into an engine's Fuel in is the same: `refused: the Engine burns fuel`.
- **Burning.** A Fluid Tank feeds a burner it touches or that a pipe from it reaches a fuel inlet of, as a fuel tank does.
  Burners draw on the pool first, then on the feeding tanks that hold fuel (`Construct.FuelLeft`, `ConstructFluid.Burn`); the
  gauge and the driver's HUD count both. A tank of water reaches the engine, and the engine has no fuel.
- **Between constructs.** A world pipe works as a pipe does, at the same flow rule. A fluid hose first moves the pools as
  before, then from the first tank with something in it to the first that has room for it (`ConstructFluid.MoveAny`).
- **Wrecked.** What it held is lost, and it stops working until mended. A Fluid Tank doesn't burn or cook off, even holding
  fuel. Taken off, it goes back to the inventory empty, as a part loses its settings.

**Creative only.** The configure tool sets the fluid and the fill in creative worlds. Both are `"creative": true` settings: in
survival the panel shows them read-only (Reset, Paste and Apply to selected go too), and the server refuses a request that
names one (`ConstructEdits.Configure`), whatever the client sends. A survival world building a blueprint, or pasting a copy,
leaves those values out, so its tanks arrive empty: neither can bring in what the configure tool can't set. Moving a selection
keeps them. A survival tank fills from pipes.

**The network.** A client's copy follows the server's: `CBodyKind.Fluid` (u16 body, u16 part, u8 fluid, f32 fill each), five
times a second for the constructs whose tanks changed, ordered and reliable (protocol 138). The fill is a percentage in a
float, so what a tick's sip can't register on a big tank waits in `CPart.FluidOwed` (not saved: a fraction of a litre).

**Choices made (the developer may change them):** two pipes for two ways rather than one both-way port; a mismatch refuses at
the flow, not when the pipe is laid, since what a tank holds changes; a fill set with no fluid chosen holds nothing; picking a
fluid at 0% doesn't reserve the tank; the survival blueprint rule above; no fire; no liquid weight; a Fluid Tank's fluid isn't
drawn on the part; a Bunker of fuel has no Fill, so a pipe can't fill it (a world pipe, a hose or a dock still can).

### Tank Volume

One block (`Constructs/Parts/FluidTanks/TankVolume.json`, written by `tools/constructs/fluid_tank_parts.py`; wrench, 14 kg, 400 health) with a box of space you size yourself: a Fluid Tank in every other way (role `fluidtank`, `"fluidTank": {"volume": true, "flow": 5000}`), so everything in [Fluid tanks](#fluid-tanks) holds: one fluid, Fluid in and Fluid out on the block, mismatches, burners, world pipes, hoses, creative `fluid` and `fill`.

- **Sizing: the Storage Volume's own tool.** The box is the same six settings (`sizeX/Y/Z` 0.2 to 40 m, `atX/Y/Z` up to 40 m from the block; 2 m cube above the block by default), so `ConstructCargo.HasBox` gives it the configure tool's face handles, R's corner-to-corner drawing, the panel, the reach to the box and blueprints with no new code or wire. While you drag, the look line says what that box would hold.
- **Capacity.** 1,000 l a cubic metre of the box as sized (`ConstructFluid.Capacity`): 8 l to 64 Ml, 8 kl by default, less what its open faces cost ([Sealed volumes](#sealed-volumes)).
- **Resizing.** What it holds stays: the fill percentage changes, not the litres. A box too small for that is refused and undone ("Empty it first"), and growing into another Tank or Storage Volume on its segment is refused. Survival may size the box; only `fluid` and `fill` are creative.
- **In the world.** The outline, floor marks and handles are the Storage Volume's, in blue. The fluid is a see-through box in its fluid's colour (`ConstructFluid.Colour`) rising along the block's up, with a brighter surface so the level reads. It isn't drawn while its box is being dragged.
- **Not solid.** Box and fluid collide with nothing. Only a player's look ray meets the fluid, so the look line and the configure tool's Fire work at the level. An empty box has only its block to aim at.
- **Mass.** The block weighs 14 kg, whatever the box's size. What it holds weighs too, through a bin's path (`Construct.LoadOf`, the 4 Hz load step, at the fluid's centre): kg a litre fuel 0.8, oil 0.9, water and coolant 1, steam and exhaust 0.01 (64 Ml of water is 64,000 t). A plain Fluid Tank's contents still weigh nothing.
- **Limits.** 4 Tank Volumes a construct. Flow 5,000 l/s at most, whatever the box's size. The attach tool refuses to take off one that holds anything ("empty the Tank Volume first"); the remover still deletes it, contents and all. Part replace refuses it, as for other volumes. Wrecked, it loses what it holds.
- **Survival.** It costs what its part costs (3 steel, 1 rubber), whatever the box's size. A blueprint or paste keeps the box and arrives empty.

**Choices made (the developer may change them):** the block's cost and flow don't grow with the box; contents weigh here but not in plain Fluid Tanks, which are small and would change existing vehicles; one block, no size classes.

### Sealed volumes

A Tank Volume or Storage Volume is meant to sit inside walls. Code: `Source/Constructs/ConstructSeal.cs`; numbers: `Constructs/Sealing.json`.

- **The check.** Each of the box's six faces is tested when the construct changes: a grid of points on the face (one a metre, 3 to 6 a side), each covered if a solid box of the construct's own lies across it within 0.35 m either way. 90% covered seals the face. The result is a 6-bit mask, `CPart.SealedFaces`, worked out in `Construct.Rebuild` (a part added, removed or shot off) and again when a non-steering joint has moved, on the server and each client alike, so nothing goes on the wire and nothing is saved. Terrain doesn't seal: only the construct's own solids.
- **The cost.** The mask is read at the bins' 4 Hz. A construct with no such volume costs one check. One check took 0.15 ms at 126 parts, and it allocates nothing.
- **A box being sized** waits half a second after each change before it can pour (`Construct.SealWait`), so dragging or typing costs nothing.
- **The look line** says what is open: `Tank Volume: 3 of 8 kl water - 2 faces open: holds 83%`. With a tool out, open faces are outlined in red.

| Penalty | Tank Volume | Storage Volume |
| --- | --- | --- |
| Capacity, all six faces open (each face a sixth of it) | -50% | -10% |
| Pour rate, of the box a second | 4%, at least 20 l/s | 0.4%, at least 10 units/s |
| Level over an open side's lowest point before it pours, of the box's height | 2% | 25% |
| Acceleration ignored below, and how much of the rest counts | 2 m/s2, all | 5 m/s2, 60% |
| Fire eats, of the box a second | 2% | 0.5% |
| What can catch | fuel, oil | wood, planks, cloth, rubber |

1. **Capacity.** Over its new limit, the rest pours out.
2. **Tilt.** Down is apparent gravity. The level is the fill's share of the box's height along it; if the lowest corner of an open face is under the level, it pours. So an open bottom empties a box, an open side drains a tank at rest, and an open top only matters when tipped or braked.
3. **Acceleration.** The same check with the body's acceleration taken off gravity (`Construct.Accel`), so hard braking throws contents out and a parked base is safe.
4. **Where it goes.** Off the open face's lowest corner, clear of the rim. Fluid goes into a tank under it (a part, or a Tank Volume's box), else it is lost with a spill splash. Bulk goes as a tipped scoop's does (`ConstructScoops.PourVolume`): into a store under it, heaped ground for dirt, stone and sand, or Material Bundles, in lumps of 40 or more. Stored parts stay put.
5. **Fire.** An incendiary round or flame landing in or at an open box of something that burns lights its block (`ConstructSeal.IgniteAt`, `ConstructWorld.CanBurn`). The fire then eats the contents and lasts while they do; a Tank Volume of fuel wrecked burning cooks off. A plain Storage Volume's block doesn't otherwise burn.

**Old builds.** The mask needs no data, so saves and blueprints load unchanged and every box is checked on load. An open box over its new limit starts to pour within a quarter of a second of the world starting, and an open side or bottom drains what stands above it.

**Cargo Volumes** aren't included: a logic part that freezes bodies, with no contents to lose.

**Choices made (the developer may change them):** six equal faces; contents in a face's 10% gap don't leak; parts in a Storage Volume aren't spilled, though the capacity limit stops more going in; a volume's pour goes into its own construct's stores if one is under it.

### Link type from the port (at01)

Board item dev-link-auto-type-at01 (the developer, 2026-10-31; decision 20 amended). The linking tool has **no type choice**:
its outer ring holds only the modes (`SandboxLinkWheel`: Route links, Delete links, Move node, Re-route, Loom, Highlight all). The
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
   degrees a bend (developer), shaft junctions need a T gearbox (developer); 5 cm node grid; 8 m a segment and no limit on a whole link (user, 2026-11-04, was 24 m and 32 nodes);
   segment health 20/40/80/250/80; a cut fuel pipe leaks 0.5 l/s; a chute moves a load (10 bulk) or one part a second.
2. Picking a link type: none any more (board item dev-link-auto-type-at01): the port clicked gives it. The outer ring keeps the modes.
3. Power cables add to the frame circuit (the frame still carries power, so every earlier construct works); a cable
   carries the circuit both ways.
4. A shaft link is a drive-line edge with no ratio (each U joint keeps 99%); chain and belt drives stay devices.
5. A link with no nodes is checked like any other (it may not pass through a part); migrated wires and blueprint links
   with no nodes are straight and checked for reach only.
6. Repairs are instant with the attach tool; junction ratios are even (the configure tool can make them
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
- **The rules** (`ConstructRoutes.WorldPathRule`, one rule for the server and the preview): the construct links' span
  limit (8 m), angle rules, clipping grace at the ends and embedding forgiveness, checked against every
  construct near the path (in its rest frame) and against the static world by rays (a segment lifted by the embedding
  depth must not hit anything; a world node must not sink deeper than it). A world node must sit on a surface (within its
  radius plus 15 cm). The end must be in reach of the player; the start may be any distance behind.
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
- Links keep the construct links' limit (8 m a span, no limit overall); hoses pick up with E only (not LMB).
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
(`SandboxAim.Remover`: a red outline with the reason when it can't; the marks themselves are the red outlines). The HUD says
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
("salvage it with the attach tool"). It gives nothing back. The map can turn it off for survival with
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

## Control modules

A seat controls nothing by itself: a car with a seat and no module doesn't go, and a gunner's seat doesn't work the gun
beside it. A seat has one output. One data link from it into a **module** carries the sitter's controls, and the module
breaks them out. Old saves and player blueprints have no modules: they stop driving until they are rewired. The shipped
blueprints have them. Code: `Source/Constructs/ConstructModules.cs`; parts: `Constructs/Parts/Controls` (written by
`tools/constructs/control_parts.py`, in Seats and Logic > Controls).

**Driving Module.** Link the seat into it, and its `drive` output to any one part of the drivetrain: an engine, a
gearbox, a steering pivot, a thruster or a hub motor (each has a `drive` port, which the picker offers alone for this
link and hides for every other). Those two links are all a car needs: that seat is now `Construct.DriveSeat` and everything
it did before runs for the whole construct: throttle, steering, brakes, the starter, shift keys, hover and lift, skid
steering, a steering differential, the jump drive. The drive link carries no signal into the part it ends at: a wired
gear signal or throttle on the same part works as ever.

Its other outputs are the same controls one by one, for a builder who wires by hand instead:

| Output | Value |
| --- | --- |
| `drive` | 1 while someone sits; as a link, the whole drive |
| `steer` | -1..1, left is plus, eased, less at speed (straight into a steering pivot) |
| `throttle` | -1..1; 0 while Back brakes a car rolling forward |
| `brake` | 1 on Jump, or Back while rolling forward (a Brake part) |
| `reverse` | 1 while it goes backwards |
| `handbrake` | 1 on Jump only |
| `run` | 1 while someone sits (an engine's run port) |
| `gear up`, `gear down` | 1 while Reload, Sprint is held (a Gear Selector's input) |
| `lift` | -1..1: Sprint, Crouch |
| `left`, `right` | Each side's drive for skid steering, -1..1 (a hub motor's input) |

A construct wired only by hand has no `DriveSeat`: its engine runs on its wires, as a seatless one does. Forward still
comes from the module's seat (`Construct.ControlSeat`), or the first seat on the body.
A steering differential takes no wire, so a tank wired by hand steers with `left` and `right` into hub motors, and a
differential's steering needs the `drive` link. Thrusters' yaw mix likewise comes from the `drive` link; by hand each
thruster takes its own signal.

**Turret Module.** Link the seat into it, and its `guns` output into each gun of the turret. A **turret** is a gun and
the turntables and motor hinges under it (`CGun.AimJoints`); several guns on one module all follow the same look. The
sitter's look points those joints, overriding wires on them, and Fire fires the guns. A gun with no joints under it just
fires. A seat need not ride the turret: one on the hull works a turret on the roof, and looks all the way round.

Every seat links to its own module, so one construct holds any number of turrets: gunner seat 1 on turret A, gunner seat
2 on turret B, a driver driving. A seat linked into several Turret Modules works one at a time: **Aim** steps to the next
(the seat's `State`), so one seat can have several turrets and a driver can have a hood gun.
A Turret Module's outputs: `guns`; `fire`; `yaw`, -1..1, half a turn either way from the seat's front, right is plus;
`pitch`, -1..1, straight down to straight up; `aim`; `reload`. `yaw` into a turntable the seat stands on turns it towards
the look and stops; `pitch` into a motor hinge sets its angle. Wire these with the `guns` output off if you want your own
control. `guns` reads -0.2 while the module is the seat's turret (no trigger; it tells clients which one is in use) and 1
while firing.

**Saying why.** The look line and the seated HUD say why a seat does nothing, in the usual format: `Seat: free - no
Driving Module linked: this seat controls nothing - F: sit`; a module says `no seat linked into it` or `drive output not
linked`. A passenger's seat on a driven vehicle has no fault. `ConstructModules.Problem` holds all of it.

**Network and saves.** Nothing new travels: the links are ordinary data links in the routes every peer has, so each peer
works out `DriveSeat`, `ControlSeat` and `TurretMods` in `Rebuild`, and blueprints, saves, joiners and selections carry them
as they carry any link. The protocol went to 139 because older peers would disagree on who drives. Modules are not
electrical: a flat battery stops an electric motor, not the seat's controls. Output ports past the first two aren't
stored: they are worked out from the seat's keys when read (`ConstructModules.Output`), so nothing allocates and a module
costs nothing while nobody reads it. A capital part's data port is on its top face, so a link reaches it.

### Choices made (the developer may change them)

1. **One link to the whole drivetrain.** The `drive` output hands the construct over as a seat did, rather than feeding six wires; single outputs are there for those who want them.
2. **A Traction City's bridge moved** to the deck's front corner, because a link reaches 24 m and that is as far as a drive part is from a 128 m hull's edge.
3. **Modules need no power** and are a tier 1 Assembler recipe (steel and rubber, no circuit board), so a first car doesn't wait for electronics.
4. **Aim steps turrets** because Fire is the gun and R is the gearbox on a seat that drives too; the `aim` output still reads it.
5. **One size.** A capital hull takes small parts through a size adapter, as it takes a seat.

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

**Spawner parts** (`"logic": {"kind": "botspawner"}` / `"zombiespawner"`, role logic, the attach tool): settings from the
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

Every sound is synthesised at start-up (`Source/Audio/SynthBuild.cs`) and plays on the mixer's **Building** row (F8, key `build`), on 8 one-shot and 3 loop voices of its own (`Audio.Build.cs`), so building never takes a gun's voice. `BuildSounds.Play(kind, position, size)` is the one call: it plays on this peer only and nothing goes over the wire. Same-kind hits within 0.3 s and 7 m merge into a burst of at most 5 hits, so a paste of 200 parts is a short rattle. A clunk's size class comes from the part's mass (small under 40 kg, medium under 1.6 t, large under 63 t, else capital) and its flavour from `material`. Ticks for the ghost, node clicks and refusals play where the input knows them. Everything else comes from `Game.SandboxBuildSounds` (once a frame, the same on host and client), which watches what every peer already has: part and link counts per body, bodies appearing and going (`ConstructWorld.GoneRing`), and the hands in the state tail (edits in progress give the attach tool's arc and the attach tool's ratchet, an attachment that ends near its finish a tick, the physics gun's hold its cues). A change only sounds with a player near it holding a tool other than the physics gun, so a part shot off, a joiner's world and machines stay quiet. New bulk actions need no call. `size` is a mass in kg.

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
- **Item parts only**: attach tool and physics-only parts cannot be ghost-attached from the hands (cargo volumes, decision 21),
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

A construct holds at most 8,000 parts, 24,000 links, 255 joints and freeform attachments (64 joins), 256 wheels and track rays,
256 spring feet, 64 guns, 254 routed links and 3,000 link nodes, 1,024 wires ([Size classes](#size-classes-the-capital-parts)
says what limits each). 4 inputs a part, wires up to 8 m (12 m between constructs, 64 of those), 32 couplings, 16,384
contacts a tick (each rigid group's points against one thing thinned to nine a normal). Fixed arrays throughout: the self-test drives a buggy round the yard for 120 ticks and
measures **no allocation** (with the turret turning, the lift and the motorised gate moving and their logic running);
the yard's constructs take about 0.1-0.4 ms a tick in the container. The logic step is a few spans on the stack. Sleeping bodies cost
nothing. Other modes never touch any of it (every hook returns at once outside Sandbox; the seeded mode digests are
unchanged).

## Self-tests

The **Flexible joints** section (`Source/App/SelfTestFlexJoint.cs`): the parts and the joint cap; a beam on a rubber joint droops where load / stiffness says, a wrecking ball takes it to its stop past its rating and off again it comes back with little overshoot; softer settings droop further, a signal locks it; kicked every way it stays inside the circle and a ball joint twists; a wrecking ball on four parts at both ends of their stiffness, kicked at random, stays finite, in one piece and settles, and a free 30 kg block carrying one stays sane; a block on the leaf by the attach tool, socket attachments on and off the grid and a freeform attachment hangs and swings alike; a motor hinge beyond it works, a wheel beyond it is a plain part; a player stands on the arm with both movers and bends it; a chute over a moving motor hinge, piston, turntable and ball joint carries bulk, one over a hinge carries parts; a piston past a section's reach stops it with the reason and it carries on coming back; a turntable stops one at 135 degrees, tears it past that with a hint, and the attach tool mends it; a cargo hose moves a load a second and pops out at its length unhurt; nothing allocates. **Flexible joints network**: a real host, a client with lag and loss and a late joiner see the same joints, axes, angles and strain, a torn chute, and a cargo hose the client plugged in carrying loads.

The **Structural parts** section (`Source/App/SelfTestStructureParts.cs`): every file in `Constructs/Parts/Structure`
loads (none refused anywhere) and keeps the cell rules; each one goes onto a 0.25 m cube by every socket on all six
faces in all four spins (every part reaches all 24 turns; about 53,000 placements), its origin always on a whole
centimetre and its cells on the cube's lattice; every socket takes a copy of the same part, which joins on; each shaped
box turns exactly with its part in all 24 turns; armour against the real guns' damage (armour plate stops every SMG,
rifle, LMG and buckshot round and pistols up to 40, lets marksman and sniper rounds through; heavy armour stops every
small arm up to 68 and lets bigger sniper rounds through; plate steel stops buckshot, not rifles; sheet steel and glass
stop nothing; the mounted MG is stopped by armour plate, the autocannon, cannon and rockets get through heavy armour);
the materials ladder (glass to heavy armour) stops more at each step; a round down through the Gun Port Panel's slit goes
through while one beside it hits the plate; the fitting checks and blocked hits allocate nothing; and the inventory lists
every structural part under its group and section, with the old blocks after the new beam sections.

The **Construct weapons** section (`Source/App/SelfTestConstructWeapons.cs`, milestone 3): the weapon part files load
(eight guns, the five heavy ones among them, the gun parts, the boxes) and ten kinds of bad file are refused for the
right reason; a rifle gun built part by part with the tools (the attach tool refused on a gun part; its problem goes from "no
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
gun; five more receivers make a 3x3 with the barrel and magazine moved on at 3x (9x the damage, 9 a shot); the attach tool on
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
wheel that fell off the buggy as a wreck is salvaged (timed) into the inventory with the
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
hold-flick-release takes out the attach tool (east), physics gun (north-north-east), linking tool (west-north-west) and hands (south), the current tool is
marked when it opens, letting go in the dead zone cancels, the scanner says "coming later" and changes nothing, the
north-east takes out the physics gun, a menu and
death close it without choosing (and the still-held key doesn't reopen it), and it doesn't open in a seat; 240 headless
frames with the pointer going round (the linking tool's outer ring among them) draw without an ImGui error and allocate
nothing; and a real client's pick reaches the server.

The **Sandbox tool wheel outer ring** section (`Source/App/SelfTestSandboxWheelRing.cs`): the outer ring's arcs (every
option's middle and edges for 13, 2, 3, 9, 1 and 10 options, centred on the tool, the far side of a partial arc picks
nothing, the arc or full circle choice); the linking tool's options are the modes only (route, delete, move node, re-route) and the highlight toggle, with no link type and no arc; in an offline Sandbox game pointing at
the linking tool shows its ring with all nine, animating in; flicking out onto each option and letting go takes the linking tool out with it; letting go on the
tool keeps the last option (marked on the ring), and in the middle cancels; an attach tool given two test modes picks from its
arc, and outside the arc the pointer turns the inner ring; and every tool's and option's name, the title and an over-long
name fit the centre circle at UI scales 0.75 to 2 (shrunk, or cut short only for the over-long one). The third tier (a test set with a five item arc on the attach tool slot, since the pipe lost its styles): five items span 125 degrees centred on
the option; resting on it opens the arc with nothing picked; entry and keep distances (no flicker); an item calls `ChooseSub`,
letting go on the option calls `Choose`; moving to the next option or inward closes the arc; the "Sandbox tool wheel" section
draws it with a sub-option hovered and checks it allocates nothing.

The **Sandbox physics gun** section (`Source/App/SelfTestSandboxPhysGun.cs`): the model, the wheel slot and the tool
byte; in a bare construct world on the yard a grabbed block is pulled onto the beam's point (2 m up) and held in the
air, the wheel's distance moves it 3 m out, a snapped turn is reached, the snap maths (steps of 45 degrees, a snapped
turn stays put); a frozen block doesn't move in 2 s against gravity; with a second player's block frozen too, player
0's unfreeze-all frees only theirs (which then falls); a second player can't take a held block; grabbing a frozen one
unfreezes it; a construct with someone unfriendly aboard, a pinned one and one too heavy for survival are refused; the frozen state
round-trips through the structure; survival's contested rule refuses the physics gun, the attach tool and the linking tool
("Owned by Rival - defended") and allows them in creative, with the owner away, on nobody's and on your own; nothing
allocates over 30 ticks (unused, and holding); and over a real loopback a client grabs, steers and freezes a loose part
on the server, the host can't take it, the host's own beam (its held construct and grab point) reaches the client, and
the client's unfreeze-all frees it.

The **Sandbox tool models** section (`Source/App/SelfTestSandboxToolModels.cs`): every tool file loads as one piece with
its hands in reach, every tool in the game and every tool still to come has a model, and 13 kinds of bad file are refused
for the right reason (two models for one tool among them); in first person each model draws its boxes (plus sparks and a
beam in use) with both hands on their grips, low on the right of the view, and the equip drops the old tool before the
new one comes up and hands back to the gun after the drop; in an offline Sandbox game a bot's attach tool (attaching: its
sparks), a carried wheel and the local player's linking tool are drawn in third person instead of the gun, the scene graph
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
| Linear Actuator, Shaft Linear Drive, Free Slider | See [Linear joints](#linear-joints) | |
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
| Seat (`seat` role) | 1 while someone sits in it; a link from it into a module carries its sitter's controls | - |
| `button` | 1 for half a second after Interact | - |
| `switch` | Interact: 1 or 0 | - |
| `lever` | Interact moves it: middle 0, forward 1, middle 0, back -1 | - |
| `xor`, `nand` | on with an odd number of inputs on; off only with every input on | - |
| The rest of the logic parts | [Logic blocks](#logic-blocks) | - |
| `analoglever` (Analog Lever) | Its value, 0 to 1, by the configure tool's output range ([below](#the-analog-lever)) | - |
| `plate` (Pressure Plate) | 1 while a player stands on it | - |
| `drivemodule`, `turretmodule` | See [Control modules](#control-modules) | - |
| `reader` (Input Reader) | 1 while the sitter of the seat wired into it (unwired: the seat that drives) holds its key | key: fire, aim, reload, jump, sprint, forward, back, left, right |
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
or refuses with the reason: "take out the linking tool (5) to wire" (bare hands, a gun, the attach tool),
"get out of the seat first", "too far away", "a Foundation has no output", "a Switch takes no input", "an input reader
reads a seat: wire it from one", "those are already wired", "a Delay takes at most 1 input", "a wire reaches at most
8 m", "at most 64 wires a construct". Aim on a part cuts every wire on it; R on a logic part cycles its setting. Wires
are drawn only while the linking tool is out (through the parts, green while their signal is on, a yellow block at the
input end). Wiring is instant (fitting a part is what takes time).

### Passengers, riding and running people over

- The seat linked through a Driving Module to the drivetrain drives; any other seat carries a **passenger**, pinned to it like the driver. A seat
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

### Logic blocks

The parts below are written by `tools/constructs/control_parts.py` (Constructs/Parts/Controls), in Seats and Logic: Controls, Sensors, Displays, Gauges, Logic gates, Memory, Wireless and bus. Their code is `LogicBlocks.cs`, `LogicInputs.cs`, `LogicDisplays.cs`, `LogicText.cs` and `ConstructRadio.cs`. Each takes one branch of the logic step and allocates nothing. What a block must keep is a setting (a Counter's or Memory Cell's `value`, so a blueprint, a world save and a joiner carry it) or its `State`, which a world save keeps. Named ports show in the linking tool's picker.

| Part | Kind | Ports in; out | Puts out | Settings |
| --- | --- | --- | --- | --- |
| XOR Gate, NAND Gate | `xor`, `nand` | up to 4 | as the gates | - |
| Math Block | `mathblock` | a, b | a op b; dividing by 0 gives 0 | `operator`: add, subtract, multiply, divide, min, max; `offset`: b while b is unwired |
| Latch | `latch` | set, reset | on from set to reset; reset wins | - |
| Counter | `counter` | up, down, reset; count, at limit | counts rising inputs | `value`, `limit` (0: none), `wrap` |
| Memory Cell | `memory` | value, store | what value was when store rose | `value` |
| Filter | `filter` | 1 | its input clamped, smoothed, then rate limited: good on a hydraulic valve | `low`, `high`, `seconds`, `rate` |
| Multiplexer | `mux` | select, 1 to 6 | the input select names, rounded (unwired: 1) | - |
| Random | `randompulse` | 1 | a new number each rising input | `value`, `low`, `high` |
| Altitude, Heading Sensor | `altitudesensor`, `headingsensor` | none | metres; degrees clockwise from north, of the part's front (-z) | - |
| Tilt Sensor | `tiltsensor` | none; pitch, roll | degrees | - |
| Level Sensor | `levelsensor` | none; level, watch | how full, %: a fluid tank's or volume's fill, the construct's fuel from a fuel tank, a store's bulk or slots | - |
| Damage Sensor | `damagesensor` | none; hit, watch | on for its `hold` after the watched part lost health | `hold` |
| Occupancy Sensor | `occupancysensor` | seat | on while that seat, or unwired the drive seat, is taken | - |
| Wireless Sender, Receiver | `radiotx`, `radiorx` | 1; none | the nearest sender on its channel that reaches it, 0 with none | `channel`; a sender's `radius` |
| Data Bus, Bus Breakout | `busin`, `busout` | 1 to 8; 1 to 8 | below | - |

- **Watch links.** A Level or Damage Sensor's second output, `watch`, carries no signal: a link from it ends at any part at all, on the port "Watch this part" (`ConstructRoutes.WatchPort`), and that is the part the sensor reads. It saves in a blueprint as the port `watch`. Same construct only.
- **Data Bus.** Eight signals into a Data Bus, one data link to a Bus Breakout, eight signals out, a tick later per end. The link is an ordinary data link, so it lies in a Loom or a tray like any other. A breakout reads a Data Bus on its own construct; across constructs its outputs are 0.
- **Wireless.** A sender is heard between constructs. Senders are gathered once a tick into a fixed list of 256, so a signal takes one tick more than a wire. Channels aren't private: any receiver on the channel in range hears it.
- **Exact numbers.** A pose's signals are tenths up to 12.7. These parts also have `CPart.Reading`, which reaches clients exactly: all of them in a CBody, and what moved five times a second in `CBodyKind.Reading` (protocol 153).

### Keys and panels

- **Numpad** and **Code Lock** have twelve real keys (`"use": true` boxes with `"key": "7"`). Aim at a key and press Use: `CActKind.KeyPress` names that key, so only it glows. Use on the screen or the body opens a small typing box: the view stays, digits go in, Enter accepts, Esc leaves. A Numpad's Accept puts the number out (`decimals` places the point) and pulses `accepted`; Clear wipes the entry and the output. A Code Lock's Accept with its `code` turns it on, for `hold` seconds (0: until Clear); a wrong code turns it off. `invert` is on until opened, for wiring straight into a hinge's Lock. Clients only ever hear how many digits were typed. Anyone in reach may press keys. The code is a setting, so it has the configure tool's owner rule, and it does travel in a CBody.
- **Rotary Knob** and **Slider** are Analog Levers of another shape (`"rig": "knob"` or `"slide"` with `"travel"`): the same drag, wheel and settings; a knob's `snap` starts at 0.1, its stops. A knob's mouse up raises it.
- **Toggle Switch** and **Rocker Switch** are switches, **Mushroom Button** a button; `"rig": "flip"` and `"press"` move their boxes by whether the part is on. Button and Switch both have `latched` and `hold`.
- **Key Switch** turns for its owner, for anyone on a construct nobody owns, or with a **Key** in your hands; any Key turns any Key Switch.

### Displays

Each has a panel ([Display panels](#display-panels)) whose `source` is a `logic.*` entry of `PanelSources`. Small, Medium and Large. Everything shows on any peer; a part with no power says so.

| Part | Source | Shows | Settings |
| --- | --- | --- | --- |
| Number Readout | `logic.readout` | the sum of its inputs, as big as it fits | `decimals`, `unit` |
| Bar Gauge, Tall Bar Gauge | `logic.bar`, `logic.barv` | a bar over the whole screen, `low` to `high`, no number | `low`, `high`, `ramp` |
| Dial Gauge | `logic.dial` | a needle over 270 degrees | `decimals`, `unit`, `low`, `high` |
| Graph Screen | `logic.graph` | the last 30 s over the whole screen, a sample each 0.5 s, no number; `low` = `high`: it fits the line | `low`, `high`, `ramp` |
| Light Bank 4, 8 | `logic.lamps` | a lamp an input, labelled | its text: labels, comma-separated |
| Text Sign | `logic.sign` | its first message; with a wire on, its second | its two messages |
| Compass | `logic.compass` | a tape, the way you face looking at it | none, no wiring |
| Altimeter | `logic.altimeter` | how high | `decimals`, `unit`, no wiring |
| Speedometer | `logic.speedometer` | a needle, and the speed | `units`, `low`, `high`, no wiring |

- **Colour.** Every display has `ink` (a colour; black: its own colours): text, needle, trace, bars and lit lamps take it; the frame keeps the state's colour. Bars and graphs have `ramp`, how the colour follows the value: display colour, green yellow red, gradient, heat (`PanelRamp`). Both are plain settings: saved by key, in blueprints, sent in CBody.
- **In the inventory** a display's tile looks at its screen and shows the real panel: `LogicDisplays.Preview` fills the part's own card from a scratch part with a typical reading, and `PanelRender.DrawOnce` draws it into the tile, so a preview can't drift; made once with the atlas, nothing a frame.
- **`PanelCard.Draw`** picks a shape; `Value`, `Trace`, `Lamps` and `LampBits` set it, and `Fit` makes one line as big as the panel takes. A needle or a graph's line is a row of small upright quads, so a panel still has one shader and one vertex format.
- **A graph** is sampled on each peer that draws, whether or not it is in view, into `CPart.Trace`. A joiner's starts empty.
- **Text** is on the part (`CPart.Text`, `Text2`; up to 60 ASCII characters). Use on a sign or Light Bank opens the typing box on what it shows; the configure tool's panel has a box for each. Both are `CActKind.PartText`, under the configure tool's owner rule with no tool needed. A blueprint step has `"text": ["one", "two"]`. A world file holds it as settings under `text0.0`, `text0.1`... three characters a float, which an older reader skips.

### The Analog Lever

A number from 0 to 1 set by hand, for throttles and speed settings (`Source/Constructs/AnalogLever.cs`, the hand in
`Source/App/Program.SandboxLever.cs`; `tools/constructs/control_parts.py` writes Analog Lever, Medium and Large, in
Seats and Logic > Controls). Wire it into any data input: an engine's throttle port, a piston (0..1 of its travel).

- **The hand.** Hold Use (F) on it and move the mouse: the view stands still, the handle goes the way the mouse goes on
  the screen (seen end on, mouse up raises it), and letting go leaves it there. That works with every tool, and with a
  gun or bare hands: Use fits and removes nothing. The exceptions are the attach tool's join (a part touching another
  construct), and the physics gun while it holds something. LMB does the same with the **Hands** out: every other
  tool's LMB is its own, and a gun fires. A press with no move changes nothing.
- **In a seat** the same works on a lever in reach of the seat (3.2 m). A press with no move is Use as ever: out of the seat.
- **The wheel** nudges the lever under the crosshair by 0.05, or its snap step: with the Hands, the attach tool or no tool,
  and nothing held; any tool in a seat. While dragging, any tool.
- **Settings** (the configure tool): `value`; `snap` (0: smooth); `low` and `high`, what it puts out at 0 and at 1, so
  1 to 0 or -1 to 1 work; `spring` and `rest` (off by default): let go, it glides back at 1 a second. Dragged or set,
  the value is a setting, so blueprints and world saves keep it.
- **On the wire** (protocol 133). The client sends `CActKind.AnalogLever` at up to 10 Hz, every 0.2 s while it holds still
  (a spring waits 0.5 s past the last one) and a last word on letting go. The server checks the reach (use reach plus
  1.5 m, as you may be moving) and the snap step, sets the setting, and sends `CBodyKind.Analog`. Anyone may use a lever,
  as any lever; the configure tool's slider has the owner's rule. The hand's own PC shows its own value at once and for
  0.6 s after letting go, so nothing snaps back; everyone else eases towards what they hear. A joiner gets it in `CBody`.
- **The handle** is drawn from the value, not from the part's signal (a pose's signals are in tenths): a `"lever"` rig
  box tilts about the part's x through its `"pivot"`, 35 degrees either side of upright, upright at 0.5. It adds no
  collision, so a hidden box stands where it swings for the crosshair.
- The lever held is a part index another player's edit can shift, so it is stamped when taken and checked each frame:
  a hand on a lever that changed just comes off.

### The Nudge tool

Moves and turns **whole constructs** exactly, by a triad of arrows and a set of arcs (`Source/Constructs/ConstructNudge.cs`, the client in `Source/App/Program.SandboxNudge.cs`, `Source/Game/GameSandboxNudge.cs`, the ring `SandboxNudgeModes`, drawn by `NudgeRender`; protocol 156). It is straight up on the tool wheel.

- **Its target** is everything the Selector has selected (every body with a part selected: the parts stay fixed in their bodies; a single part is never moved on its own), else the construct you click. RMB lets a clicked one go.
- **Move.** The triad sits on the pivot, drawn over everything: X red, Y green, Z blue. Aim at an arrow, hold LMB and drag: the view holds still and the mouse's travel along the arrow on screen is the move. An arrow end-on to the view can't be taken.
- **World or local (R).** World: **Z runs down along gravity** (a planet's, a generator's: `Nudge.Axes`), X as near the map's X as that lets it be, Y square to both. Local: the body's own axes under the same names, so an unturned body's are the world's.
- **Rotate.** Rings take the arrows' place, one about each axis; aim at one and drag round it. **Rotate snap** turns in steps of the snap angle, **Rotate free** at any angle.
- **The ring** is Rotate snap, Move (the default, in the middle), Rotate free. **The mouse wheel** sets the move step (free, 1, 5, 10, 25, 50 cm, 1, 2, 4 m; 10 cm to start) or the snap angle (1, 5, 10, 15, 22.5, 30, 45, 90 degrees; 15 to start). They last the session. RMB or Esc during a drag puts it back.
- **The pivot** is one body's centre of mass, or the mass-weighted centre of a selection's bodies. A local selection uses the first body's axes.
- **On the server** the client asks for the whole move and turn since the drag began (`CActKind.NudgeSteer`, about 30 times a second, unreliable; `NudgeEnd` for certain when let go). A nudged body is **posed, not pulled**: `Construct.NudgeBy` holds it out of the physics, and `ConstructEdits.NudgeTick` eases it on over 0.05 s (at most 30 m/s, 6 rad/s), landing exactly on what was asked. So a step is a step, and a selection moves as one. That is where it parts from the physics gun; it shares the gun's refusals, its range (30 m, 12 m in survival), the rule for what blocks, and the holder's drawn lead (`ConstructGrab.LeadMin`, `LeadSeconds`). A body in either is refused by the other.
- **What stops it.** Only a **new overlap** with another construct, a player or the map, tested tick by tick; a piece already in something may move, and what a body carries, is coupled to or moves with never blocks. It stops there with "something is in the way". The map is tested at 128 boxes a tick, in turn, so a very big body may go a few ticks into a wall first.
- **Afterwards** a frozen or anchored body stays so; a loose one that was moved or turned freezes where it landed, to build on (`NudgeLand`; not with a driver in its seat, and not a cancelled drag). A drag that moved nothing leaves it to the physics. A coupled partner isn't moved: the coupling pulls it once the physics runs.
- **Who may.** As for any edit: your own, wrecks, loose parts and the map's; never one someone holds or someone unfriendly is aboard; the contested rule. Survival adds the physics gun's 500 kg cap and leaves anchored bodies alone (frozen ones may move). In creative anything goes.

**Self-test** "Nudge tool" (`SelfTestNudge.cs`), and "Interaction sweep" takes it to every part.

### Contraption space

A player on a construct lives in its frame (`Source/Constructs/ConstructRideMove.cs`, the look and drawing in
`Source/Game/GameSandboxRide.cs`; protocol 130). This replaces the plain ride above for people; bots keep the plain ride.

- **The frame.** A branch of `World.Simulate`, like noclip: with `MoveState.RideUp` set, the tick runs `ConstructRide.Step`:
  the capsule mover's walk, step-up, ground snap, jump and stances (`SweptMover.FrameStep`, the same code the world mover uses)
  in the coordinates of the segment under the feet, against shapes gathered there. `RideLocal` (feet) and `RideVel` (their
  velocity) are the whole of their movement; `Position` and `Velocity` are copies for whatever reads the world. The construct's
  own boxes come from its joints alone (`SegPose`), never its world pose, so nothing about its speed, turn, tilt or roll
  reaches the mover: the same inputs give the same local state at 0 and 200 m/s, tilted or inverted, on the server, in the
  client's prediction and in its replays. A segment is the moving joint's (`MoveSeg`): a freeform attachment is the same frame.
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

- **Seat types** (`SeatKind`, from where the seat is and what it is linked to): the drive seat is the **driver's**; a seat with a Turret Module, or a seat on a moving joint's
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
- **Getting in and out** keeps its rules (Use on a free seat; F gets out, beside the seat if clear), with one change on the move, below.
- **Coming down onto a construct** (`ConstructWorld.BlockPlayer`). A body coming down lands on any part's top that faces up in the
  world, whichever way the part is turned (an inverted or tipped hull too), with its centre up to `ConstructRide.EdgeHold`
  (0.24 m) past the edge, as the frame's capsule stands on an edge, and a tick's fall relative to the part always reaches the
  top, so a fast fall doesn't pass it. Without these the sideways push-out took them off the rim or beam. The landing's tick is
  drawn from where the feet were (`RideEntryDraw`), and a client's correction on a deck is eased up to 8 m (a fast deck on a
  client's copy is a round trip behind). Frozen upside down a rider stays on (level is judged by their own up). The world
  still counts: a hull that carries its rider into the ground lets them go, and a construct can't run over someone who rode it
  in the last 1.5 s (`Game.RunOverGrace`); it only pushes them.
- **Getting out on the move** (`ConstructRideExit.cs`, `Game.SandboxStand`). On a construct moving over 1 m/s, or with no ground
  under the plain exit (a hovering hull, a ship on water), the search looks for a spot on the construct first: 8 ways out of the
  seat in its own directions and the rider's up, 0.75 to 3.2 m out, nearest first, in sight of the seat before any other. A spot
  needs a floor under the feet that moves with the seat (this construct or one it stands on, a car on a ferry; not a wheel),
  room for the body in the turned frames (`ConstructWorld.CapsuleClear`) and no map geometry. The rider goes through
  `ConstructRide.End`, as if they had landed there, so they are in the frame from their first tick. With no spot they take the
  plain exit at the seat's velocity (`RideFree` as for stepping off), to the side of the construct's way, never in line with
  it. The local player's seat now comes with their own snapshot entry, not the ~10 Hz hands state, so the seat and the move
  always change together. Parked on the ground the exit is the plain one. "Ride frame" (`SelfTestSeatExit.cs`) tests this
  with a host, a client and a watching third peer at 150 ms lag, 10 ms jitter and 2% loss, plus a real buggy.
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
6. **Seats**: a seat controls what its modules give it ([Control modules](#control-modules)); any other seat is a passenger seat.
7. **Logic settings** are changed with the linking tool's R (no separate configure tool); controls (buttons, switches,
   levers) work with any hands, as Use. (Since the configure tool: R steps through the old values as presets; every
   setting is in the configure tool's panel.)
8. A **pressure plate** is pressed by players only (not by constructs or loose parts).
9. **The attach tool and linking tool** sit on 4 and 5, the kill-streak keys (Sandbox has no streaks; the Weapon slot 4
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

## Freeform attach (board item fw01, decision 25)

The attach tool has five modes on the tool wheel's outer ring round it (`SandboxWeldModes`), clockwise Rope, Freeform attach, Socket attach, Spring, Cylinder, Socket attach in the middle so a flick straight out lands on the default ([Ropes and springs](#ropes-and-springs) has the two ends): **Socket attach**, the default
(held parts snap onto sockets, as before), **Freeform attach**: position a piece by hand with the physics gun at any
angle and offset, then attach it to the vehicle exactly where it is. [Point attach](#point-attach-board-item-pw01) is Socket attach with Shift held at its first step.

### Using it

- **Two clicks.** The first LMB picks a part (outlined amber), the second LMB on a part of another body joins the two at
  their current relative position and turn. No socket, no grid. The two must come within **3 m** (`Freeform.MaxGap`), measured
  between the closest points of the two parts' boxes (`Freeform.Gap`), and each must be in the usual reach of you. RMB or
  LMB on nothing drops the first pick. A refused second click keeps it, so you can aim at something nearer.
- **Nothing is pulled** and no step spins: the two stay where they are, so the 30 m pull and the "go on while red" of
  the socket and point attachments don't apply; a red click is refused with the reason.
- **Preview** while aiming: both parts outlined and the attachment bead drawn between their closest points, green when the attachment
  is allowed and red when not, the reason on the look line. A part in your hands goes on a socket as in any mode.
- **Cutting** (either mode): the attach tool's aim (RMB) on a freeform join (within 0.6 m of anywhere along its bead, on either of its two
  parts) cuts it: the piece falls away as a body of its own where it is, moving as it moved. Destroying the anchor part
  (or the attached-on part) detaches it like any part.

### Rules (`Freeform.Check`, `ConstructEdits.FreeformRefusal`)

Two different bodies (a loose piece, a wreck, another construct); the closest points of the two parts' boxes within **3 m**
(touching isn't needed: the bead bridges it); neither part wrecked; at most **8** freeform attachments a construct (they share
`Construct.MaxJoints` with the joints, and the world's joint cap); the attached-on piece without raycast wheels, not riding
in a cargo volume and not carrying cargo; nobody driving or sitting in it (as joining). Then the usual attach tool rules: the
attach tool out, out of a seat, within reach, your own constructs or ones anyone may salvage (decision 19), the contested rule
(`ConstructEdits.Contested`) and, in survival, the attachment takes **2.5 s** (`EditKind.Weld`, checked again when done; creative
joins at once). The physics-only rule (decision 21) is unchanged. Which body keeps its frame: one held to the ground (a
foundation or pinned, not just frozen), else the one that isn't a loose part, else the one with more parts, else the
second picked; the other becomes the freeform segment. The attach tool's owner takes the result, as joining does.

### How it works (`Source/Constructs/ConstructFreeform.cs`)

A construct part still turns only in Rot24 steps on its segment's 1 cm grid. The attached-on piece (its whole body) joins the
construct as a new rigid **segment** whose rest frame sits at a fixed, arbitrary pose on its parent segment: a joint of type
`JointType.Fixed` (wire byte 5) with `CJoint.FixRot` / `FixPos` (`Construct.SegPose`), its anchor part in `Hinge` and the
attached-on part in `Child`. Everything inside the segment stays on its own Rot24 grid, so socket parts attach to a freeform
panel as usual (they go on its segment; `ConstructBuild.Blocked` compares parts in the same frame, `Construct.FrameOf`).
Nothing solves it: the physics skips freeform attachments (no slot, no chain entry, no motor), their parts' corners are **hull
points** of the body (`Construct.Rigid`), and the mass, centre of mass and inertia take them turned into the body frame
(`Construct.RestPose`, `RestBounds`; each box's own inertia turned with it). It costs what a jointed segment costs minus
the joint, and the moving-joint passes only run for constructs with a moving joint (`Construct.MovingJoints`). Collision,
damage rays and drawing already work per segment. The attachment is an edge in the connectivity flood, so losing the anchor or
the attached-on part splits the piece off where it is (`ConstructWorld.Compact` moves the body's frame to a freeform
segment when what stays hangs beyond an attachment that went, so nothing jumps). A cut marks the attachment and runs the same split.

### Network, blueprints and saves (protocol 97)

`CBody` carries the freeform attachments at its end (each one's joint, attached-on part, position and quaternion as exact
floats); `CActKind.FreeformWeld` (u16 body, u8 part, u16 second body, u8 its part) and `CActKind.FreeformCut` (u16 body,
u8 part, u8 attach) at the end of `CActKind`; `EditKind.Weld` in the hands tail. A blueprint step may be an attachment instead of a
socket step: `{ "part": "Panel", "weld": { "on": 3, "pos": [x, y, z], "rot": [x, y, z, w] } }` puts the part on a new
freeform segment, its own frame at that pose in step 3's segment frame (`ConstructBuild.WeldJson` writes one from an attachment,
`Freeform.StepPose`). Constructs aren't saved with the world yet; the construct message and the blueprint step are what
a save will use.

### Self-test

"Freeform weld" (`SelfTestFreeformWeld.cs`): a panel held at 30 degrees over the buggy's plate is attached by one
ray-through click without moving and stays exactly there (to a micrometre) while the buggy drives 30 m and turns, with no
allocation per tick; the two-click fallback joins two panels side by side; a gap of 0.6 m is refused with the reason;
a beam socket-attaches to the freeform panel on its segment; the attach tool cuts it free where it was, beam and all;
destroying the anchor detaches the panel where it was; survival's attach waits its 2.5 s; a fresh copy from the construct
message and a blueprint with the attachment step put the panel at the same pose; a real host with a separate client joins and
cuts over the transport and the client's copy agrees on the pose; the attach tool's default mode is socket attach.

### Choices made (the developer may change them)

- The gap limit is 0.25 m and a construct takes 8 freeform attachments; attach strength is a normal attach's (the anchor and the
  attached-on part take the damage).
- Cutting is the attach tool's aim on the join (RMB within 0.6 m of the bead), and it is instant (survival included);
  attaching is LMB.
- In freeform mode the held part isn't fitted (switch back to socket attach on the wheel to fit it).
- A piece with raycast wheels can't be freeform-attached (wheels roll in the body's own frame); a part on a freeform piece
  can't be socket-joined to another construct with F (attach it freeform instead).
- **Wheels** (the "Wheel welds" fix): a freeform attachment of a wheel to a suspension whose free axle sockets are within
  0.35 m and face each other (`Freeform.AxleSnap`) is done as the socket attach of the axle onto the axle at the nearest
  grid turn, so the wheel rolls and springs as a fitted as an item one does (the look line: "puts the Wheel on the
  Suspension's axle (it rolls)"). A wheel attached freeform to anything else is a **fixed wheel**: attached on, it doesn't
  roll, and the look line says so.
- A routed link copied in with the attached piece keeps its nodes in their host parts' frames.

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

## Socket attach (board item sw01, decision 26)

The attach tool's default mode, **Socket attach** (the outer ring's other mode is [Freeform attach](#freeform-attach-board-item-fw01-decision-25)),
is a three-step flow. Sockets are an alignment and snapping aid here, not a strict connection system.

1. **Pick the source socket.** With the attach tool out and nothing in your hands, the aimed body and the bodies nearest
   the crosshair (at most four, within 30 m at this step, 7 m at the next, at most 64 markers, the nearest the crosshair)
   show their sockets as a dot and a short arrow along the socket's facing, drawn bigger past 7 m: blue free, grey in use,
   orange the picked one, green a target, white the one the crosshair snaps to (within about 4 degrees). LMB on a socket
   of the piece to move (a loose body or a separate construct) picks it; the piece stays where it is. A piece that can't
   move whatever the target says so here and isn't picked: held to the ground (pinned, a working foundation), riding in a
   cargo volume, carrying cargo, someone else's. A frozen piece can be picked: the physics gun's freeze holds it, it doesn't anchor it.
2. **Pick the target.** LMB on a socket of the construct to attach to (the magnet snap), or on any flat face of one of
   its parts: the 1 cm grid point under the crosshair on that face is a virtual socket. A ghost of the piece shows its
   socket on the target point, facing opposite the target's normal, turned to smart rotation's best guess (a one-part
   piece onto a socket; others start at 0), green or red with the reason on the look line (an overlap, the ground,
   someone standing there, someone else's construct, a full construct, wheels off the grid...). **LMB goes on either
   way**: a first guess is only a guess, and the spin is what puts it right. A thin line joins a piece more than 1.5 m
   away to where it goes.
3. **Spin it about the socket axis.** Mouse X (the view holds still) or the wheel turns the ghost; it snaps to 15
   degree steps with a click on each 90 degree step and a ring of ticks round the socket (bigger on 90), Shift for free
   angles, Ctrl for 5 degree steps. R takes the next facing smart rotation ranked (else half a turn: upside down). The
   ghost stays red with the reason until it is valid, and the look line says "spin it until it fits", or, when no 15
   degree step fits, "no angle fits here" and RMB for another spot (a hint: it doesn't stop the spin, and finer angles may
   still fit). LMB confirms: only this click needs a valid pose, and a red one is refused with the reason. The attachment takes
   the piece's fit time in survival and is instant in creative; the body snaps to the ghost.
   RMB steps back one step (3 to 2 to 1 to nothing; at step 1 RMB is the attach tool's usual cut free), Esc cancels.

**Pulling from afar.** The piece may be up to 30 m from the eye (`SocketWeld.PullReach`, survival and creative); the
target stays within the usual reach of the player or the Build Gantry's range (`ConstructEdits.WeldReach`, the server's
rule and the preview's, which reads it every frame, so walking off turns the ghost red: "too far away: ..."; with the
gantry's camera on, the preview leaves it to the server). The piece is measured at its part's `ReachPoint` or the
picked socket, the nearest; a body over 8 m round at the nearest of its bounding sphere. How it travels: it doesn't.
Nothing crosses the space between: it stays where it lies until the attachment is done, then jumps to the ghost, through
whatever is in the way, exactly as a near one does, so the wire is unchanged. The picking player sees a streak along the line
for the attachment's time and a third of a second more (`SocketWeldRender.Pull`; drawn on that PC only). Decision 19's contested rule, ownership,
"someone is driving it" and the survival recheck when the time is up all stand. Picks and reach go through the frame
like any aim ([Contraption space](#contraption-space)): the look ray and the eye are the client's and server's usual ones.

**On the grid or freeform.** A multiple of 90 degrees onto a real free socket of the same kind on the construct's main
body keeps the piece on the construct's Rot24 grid: the two merge as a join does, linked socket to socket, the parts
on the centimetre grid. Any other angle, a face point, a socket of another kind, or a target on a joint's or a freeform
piece's segment joins the piece as a fixed-pose rigid segment, exactly as a freeform attachment does
(`JointType.Fixed`, `ConstructWorld.FreeformWeld`; at most 8 a construct, no wheels on such a piece). The attach tool cuts
either free again (a grid part with RMB held, a freeform join with RMB on its bead).

**Wheels.** A wheel's axle onto a suspension's axle socket always goes on the grid, at any spin (a wheel turned about
its own axle is the same wheel: it snaps to the nearest grid turn), and a rolling piece (a wheel or a suspension, no
joints of its own) onto a socket on a moving joint's segment (a steering pivot's) merges onto that segment
(`JoinPlan.Seg`) instead of becoming a fixed-pose segment: the wheel rolls, steers and springs exactly as one the
attach tool fitted. Before this fix a wheel attached at a non-90 spin, or onto a steering pivot's suspension, was held on a
fixed segment and acted rigid. An attached wheel off a suspension's axle (a face point, an off-grid angle elsewhere) is a
fixed wheel; the look line says "a wheel on it is fixed and won't roll". **Self-test** "Wheel welds"
(`SelfTestWheelWeld.cs`): the buggy's wheels taken off and attached back (socket attach at 0, 15, 90 and 200 degrees, two
of them on the steering pivots; freeform attachments 4 cm and 4 degrees off each axle) give the attach tool's four raycast wheels:
they spin when driven, the springs carry the car and compress landing a 0.35 m drop, it drives and turns as the
blueprint's buggy does, a client's copy from the construct message has the same wheels; a wheel attached to a beam is
fixed; driving allocates nothing.

**Joints (board item wh01).** An attachment onto a joint's leaf socket (a hinge's or motor hinge's leaf, a piston's or
turntable's top, a steering pivot's leaf, every actuator size) always hangs the piece on that joint, as a fitted part's part
does: the attach tool share the leaf code (`ConstructBuild.OnLeaf`, `LeafRefusal`, `HangOnLeaf`: the joint is
made at rest, or an empty one it had is used again). On the grid it merges onto the joint's segment, linked to the leaf
(a fitted part's pose exactly); off the grid (any spin) it is a fixed-pose segment whose parent is the joint's segment, so it
moves with the joint. A freeform attachment whose anchor is a joint part and whose nearest point on it is on the leaf's end
(`Freeform.OnLeafSide`) hangs on the joint the same way; one onto the base side stays rigid with the base. A piece on
the leaf that would also attach to the joint's fixed side (a free socket of it meeting one there, face to face) is refused:
"that would lock the joint" (`SocketWeld.LockRefusal`); only resting against it, as a lid on its chest, is fine. The look
line says "on the X's moving side". Two bugs were behind the report: the socket attach merged a piece onto the joint
part's own (base) segment, linked to the leaf but with no joint made, so pistons and turntables built with the attach tool
carried nothing; and `Construct.Rebuild` gave every joint whose anchor part is a joint part that part's joint type, so
a fixed-pose segment anchored on a hinge (an off-grid attach, a freeform attachment on it) turned into a second joint about the
body's origin and lost its pose: the piece showed up mirrored through the hinge. Freeform attach now keep their type.
**Self-test** "Joint welds" (`SelfTestJointWelds.cs`): every joint part on a powered, anchored rig with a lever wired
in (the lid hinge on the Chest); a panel (a door on the passive hinge, the chest's lid on the lid hinge) socket-attached on
the grid and 30 degrees off it and freeform-attached onto the leaf sits outward at the ghost's pose (a fitted part's within
1 cm and 1 degree), hangs on the joint, a client's copy agrees, and the lever drives it out and back as the blueprint-built
one moves; base-side joins stay rigid; an attachment onto both sides is refused; driving allocates nothing.

**Smart rotation learns** from step 3: confirming a grid angle other than its guess records the correction for that
part, socket kind and side, as fitting a held part does.

**Server and network (protocol 99).** The client sends `CActKind.SocketWeld`: the piece and its part and socket, the
construct, its part and socket (255: a face point, then the point in that part's segment rest frame and its facing),
and the angle in hundredths of a degree. The server checks the final pose with the same rules (`SocketWeld.Check`:
the sockets, overlap with the construct, the map and players, the merge and freeform limits) plus reach, ownership and
decision 19's contested rule, and in survival again when the attachment time is up (`EditKind.SocketWeld`, shown as
"Attaching..."). Code: `Source/Constructs/SocketWeld.cs` (rules, pose, markers), `SocketWeldFlow.cs` (the steps),
`ConstructEditsSocketWeld.cs` (the server), `Source/App/Program.SandboxSocketWeld.cs` (the input),
`Source/Render/SocketWeldRender.cs` (markers, ghost, ticks).

**Self-test** "Socket weld 3-step" (`SelfTestSocketWeld.cs`): decision 27's tools by function over every part file;
an aimed loose beam lists its markers (no allocation); its end socket on a frame block's side: the ghost aligned and
facing out; a 30 degree spin joins it as a fixed-pose segment exactly at the ghost, 90 degrees stays on the grid
(merged and linked) and smart rotation learns the correction; R and RMB step by step; a red target (an overlap) is
refused at step 3's confirm; a face point in survival takes the fit time; the construct message and a blueprint keep the pose; a real host
with a separate client joins both ways and both agree.

**Choices made (the developer may change them).** 30 m in both modes, not the physics gun's 12 m in survival: the gun
carries 500 kg live, while the attach tool stands still for the fit time and is how a heavy machine gets moved there at all.
There is still no weight limit, and the pull needs no clear path. R steps through smart rotation's ranked facings rather than a
separate list of "variants" (a socket has one facing; the spin covers the rest). Face points and off-grid angles are
always freeform segments (a face point has no socket to link). A piece held to the ground (pinned or on a working
foundation) can't be the piece moved: pick it as the target. There is no weight limit beyond the merge limits.

## Point attach (board item pw01)

The developer's request: "click on the part that's going to move, and where you click is where the weld alignment will
originate from. Then you click on the target location and have a rotation option. And then weld, like the socketed
welded tool, but based on where you click." **Point attach** is Socket attach with **Shift** (the sprint key) held at its first step: the look line and the hint say so
(`Program.PointAttachNow`). Shift counts only while no flow is under way: from step 2 on the flow goes on by itself, so you can let go, and Shift is the spin's free angle again. It has no place on the ring.

1. **The origin.** With the attach tool out, nothing in your hands, LMB on the piece to move (a loose body or a separate
   construct up to 30 m away; not one held to the ground, riding in or carrying cargo, or someone else's): the point under the crosshair on that face (on the 1 cm grid of the piece's
   main body) and the face's normal are the attachment's origin and alignment. An orange dot, arrow and cross mark it.
2. **The target.** Aim at another body: a live ghost puts the origin on the point under the crosshair, its normal facing
   the target's (face to face), green or red with the reason on the look line (a green dot marks the target point). LMB
   picks it, red or green, as the socket attach's does; the target is in the usual reach.
3. **Spin.** Mouse X (the view holds still) or the wheel turns the ghost about the target normal in 15 degree steps (a
   click on each 90), Shift free, Ctrl 5 degrees, with the socket attach's ring of ticks; R flips it half a turn. It says
   what is wrong and whether any 15 degree step fits, as the socket attach does (sockets lined up per angle). LMB joins, once it is green.
   RMB steps back (3 to 2 to 1), Esc cancels; putting the attach tool away, another mode, or the piece or target changing
   under it (the picked parts' kind and place are stamped and checked every frame) clears it.

**How it joins.** It is the socket attach with a face point on both sides: the same pose (`SocketWeld.Pose`), rules and
refusal reasons (`SocketWeld.Check`: overlap with the construct, the map and players, the merge, joint and freeform
limits, the lock refusal), the same server checks (reach, ownership, decision 19's contested rule) and the same time
(survival: the piece's fit time, `EditKind.SocketWeld`; creative at once). **Sockets lined up**
(`PointWeld.Resolve`, worked out by the client's ghost and again by the server on the same numbers): where the
face-to-face pose puts a free socket of the clicked piece part within 2 cm of a free socket of the clicked target part,
facing it within about 3 degrees, it is that socket attach (so a multiple of 90 degrees onto a socket of the same kind
merges on the grid, linked); a wheel and a suspension whose free axle sockets come within 0.35 m, facing, go axle onto
axle, so the wheel rolls and springs. Otherwise the piece is a fixed-pose freeform segment at the clicked points. A
**joint's leaf**: a target point on the leaf's end of a joint part (the outer quarter along the leaf socket's facing, as
the freeform attachment has it; `SocketWeld.OnLeaf`) hangs the piece on the joint (the socket attach's face points onto a leaf
now do the same), and a piece that would also attach to the joint's fixed side is refused ("that would lock the joint").
Drivetrain parts attached across a join work as the freeform drivetrain fix has it (fd01).

**Network (protocol 107).** `CActKind.PointWeld` at the end of `CActKind`: u16 piece, u8 part, the point (its body
frame) and its facing, u16 construct, u8 part, the point (that part's segment rest frame) and its facing, u16 angle
(0.01 degree). The server resolves the sockets itself and runs the socket attach's path (`ConstructEdits.BeginPointWeld`;
the piece's point is kept in the hands as `SrcAt`/`SrcDir` for a timed attach). Code: `Source/Constructs/PointWeld.cs`,
`Source/App/Program.SandboxPointWeld.cs` (the input; the steps are a `SocketWeldFlow` whose source is a face point),
`SocketWeldRender` (markers and ghost).

**Self-test** "Point weld" (`SelfTestPointWeld.cs`), through the real input (`Program.SweepFrame` outside an ImGui frame,
`SweepDraw` in a real headless frame with error recovery): a loose panel's clicked point onto a frame block's face,
spun 30 degrees with the wheel (R to 210 and back), joins at the ghost: the point on the block's (1 cm), normals opposed
and the spin as asked (1 degree); clicked points on two sockets merge on the grid, linked; a panel that would overlap
the construct is red with the reason, LMB goes on to the spin and the next LMB joins nothing; RMB, Esc, the attach tool put away, socket mode and the piece
removed all clear it; holding still in steps 2 and 3 allocates nothing; a real host and a separate client (lag, loss):
the client's attach lands at its ghost on both peers. Through the server path with the points the input would pick: a
panel on a motor hinge's leaf hangs on the joint and the lever turns it; a block on the leaf beside a wall on the fixed
side is refused (lock), without the wall joins; the buggy's four wheels attached back by points a few centimetres off
their hubs and the suspensions' axle faces roll, spring and steer like a fitted one; the engine attached back onto the
gearbox by clicked points drives the buggy like the blueprint's. "Interaction sweep" covers the mode too.

**Choices made (the developer may change them).** Point attach is a third ring option, not the default (the developer's
"change the default connection" was about the linking tool). Points snap to the 1 cm grid like the socket attach's face
points. Sockets "line up" within 2 cm and 3 degrees (an axle 0.35 m); only the two clicked parts' sockets are looked at.
The spin starts at 0 (smart rotation's guess is the socket attach's: there is no socket to rank).

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

