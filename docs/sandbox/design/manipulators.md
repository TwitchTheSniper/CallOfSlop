# Manipulators instead of a big physics gun (direction, user 2026-11-19)

Direction from the user; no design yet. Recorded so the queue's builders and the module sockets work share it.

## The user's words

"In survival, instead of a physics gun with upgrades, I would like instead there to be robotic arm stations or shells that are made for manipulating large parts. In queue, a new 5-meter construction shell that has the ability to use a welder and the ability to pick up and move pieces. The intent with all these changes is to move away from the unrealism of a physics gun moving pieces around at all sizes. For larger sizes, there should be a physical manipulator attached to pieces representing how it's being moved around the world. I understand for small things, the player still needs that option to move and manipulate small pieces. So for now, the small physics gun will be retained by the player. But for anything larger, we're going to move to arms and shells."

## What it means

- **Survival:** the player keeps a small physics gun for small pieces only. Its upgrades that raise the lift (today 5 t, upgrades to 80 t: docs/sandbox/TOOL-UPGRADES.md) go away or stop at the small limit. Anything larger is moved by a physical manipulator that is seen attached to the piece: robotic arm stations (cf. the Module Fitter arms in module-sockets.md, the Shell Gantry's arms, Loader Arms, cranes) or shells built for it.
- **Construction Shell (queued):** a 5 m shell (it docks in a Medium pod) with a welder and a gripper: it picks up, carries and places large pieces and welds them on. It joins the modular shell layout (Constructs/Shells.json).
- **Creative:** not stated; default unchanged (the big physics gun stays in Creative).

## Open

- Size limit not stated (only mass).
- Whether the Lift Coils should later be refunded or removed.

## The physics gun now (built 2026-11-19, branch claude/physgun-flight)

- **Survival lift: 3 t, whatever the upgrades** (user: "3t is good"). `ConstructGrab.SurvivalLiftCap` = 3,000 kg caps `ConstructEdits.LiftOf` in survival; creative is unchanged (20,000 t). A refused lift says so in the look line and the notice: "too heavy for the physics gun (4200 kg; 3000 kg at most in survival: bigger pieces need an arm or a shell)" (`ConstructGrab.HeavyHint`; free joints and racked pieces too).
- **Lift Coils kept** (default chosen): saves keep their levels and the modules stay buildable; a coil now only raises the beam's pull (`PullOf` reads the uncapped level, 30 N a kg: base 150 kN, Mk 5 2.4 MN), so it moves what it may lift faster. The upgrades panel shows "3,000 kg at most, pull N kN", the fit notice "physics gun pull N kN (it lifts 3,000 kg at most)", and the parts' text says the same (`UpgradeParts.cs`, regenerated).
- **Speed build-up in flight** (user: "the physics gun movement speed should build as the player keeps moving an object around ... only if the object is in flight"): `ConstructGrab.FlightBoost`, on the server after the contact solve. While a held piece touches nothing (no contact pushing on it, none within 0.02 m) and moves faster than 1.5 m/s against its holder's frame, its boost climbs linearly from 1 to 3 over 2 s; the beam's top speed (30 m/s) and its acceleration are times the boost. A contact, a stop (under 0.5 m/s) or a let-go puts it back to 1 at once; between the two speeds it holds. Constants: `FlightBoostMax` 3, `FlightBoostSeconds` 2, `FlightMoveSpeed` 1.5, `FlightStopSpeed` 0.5, `FlightTouchGap` 0.02. The hands in the snapshot carry it (a byte, protocol 375) so a joined holder's prediction steps with it.
- **A joined client's turn** (user: "when rotating objects, the client has strange behavior"): the turn F and the mouse ask for reaches the server a round trip late on its own unreliable message, ~30 a second in bunches. Two causes, both fixed: the client's prediction eased its path and clamped its lead by the body's origin, so the turn it led by became a position error (metres on a long piece) that dragged the grab point off the crosshair and fought the prediction's spring; now both work on the grab point and turn about it (GameSandboxPhysGunSim.cs). And the server took each step of the turn at once, spinning the piece in jerks (8 rad/s peaks for a steady 2): a joined holder's asked turn is now followed over 0.05 s (`ConstructGrab.SteerSmooth`, `SandboxHand.GrabRelWant`); the host's own turn stays immediate. Window tool: `swing RATE SECS DIST turn` turns the held piece as F and the mouse do.
