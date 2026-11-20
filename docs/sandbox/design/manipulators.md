# Manipulators instead of a big physics gun (direction, user 2026-11-19)

Direction from the user; no design yet. Recorded so the queue's builders and the module sockets work share it.

## The user's words

"In survival, instead of a physics gun with upgrades, I would like instead there to be robotic arm stations or shells that are made for manipulating large parts. In queue, a new 5-meter construction shell that has the ability to use a welder and the ability to pick up and move pieces. The intent with all these changes is to move away from the unrealism of a physics gun moving pieces around at all sizes. For larger sizes, there should be a physical manipulator attached to pieces representing how it's being moved around the world. I understand for small things, the player still needs that option to move and manipulate small pieces. So for now, the small physics gun will be retained by the player. But for anything larger, we're going to move to arms and shells."

## What it means

- **Survival:** the player keeps a small physics gun for small pieces only. Its upgrades that raise the lift (today 5 t, upgrades to 80 t: docs/sandbox/TOOL-UPGRADES.md) go away or stop at the small limit. Anything larger is moved by a physical manipulator that is seen attached to the piece: robotic arm stations (cf. the Module Fitter arms in module-sockets.md, the Shell Gantry's arms, Loader Arms, cranes) or shells built for it.
- **Construction Shell (queued):** a 5 m shell (it docks in a Medium pod) with a welder and a gripper: it picks up, carries and places large pieces and welds them on. It joins the modular shell layout (Constructs/Shells.json).
- **Creative:** not stated; default unchanged (the big physics gun stays in Creative).

## Open

- **Small limit: 3 t** (user, 2026-11-19): the Survival physics gun lifts up to 3 t; heavier pieces need an arm or a shell. Size limit not stated.
- Whether existing upgrade items are refunded, removed or repurposed.
