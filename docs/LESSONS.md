# Lessons

What went wrong before, and the rule that prevents it. Moved out of `CLAUDE.md` on 2026-11-11 so it isn't loaded into every session: read only the themes that touch your task (`grep -n '^\*\*' docs/LESSONS.md` lists them).

Folded by theme on 2026-11-03 and 2026-11-09; the dated stories are in `git show 4a567bec:CLAUDE.md` and `git show c6b159b26:CLAUDE.md`. Add new entries at the end, one line each: date, what went wrong, why, the rule.

**Frame state and crashes**
- Per-frame state holding indices into a construct is stamped when taken and checked against the construct as it is when used (`ConstructRender.GhostLive`, `Program.LinkStamp`, `LinkStateCheck`, `LinkHitUsable`): a tick, a packet or the other peer can shift parts between input and draw, and a hit may name no part.
- ImGui is called only between NewFrame and Render. Input stores what to draw and the ImGui pass draws it; `Hud.CanDraw` skips and counts anything else (a native crash otherwise, with no exception).
- Clamp bounds worked out at run time go through `SafeMath.Clamp`. Host actions, Sandbox input and its ImGui pass stay behind their guards (`Game.SandboxActGuarded`, `Program.SandboxInputGuarded`, `DrawSandboxUiGuarded`).
- After a crash, read the `[crash]` block in callofslop.log, crash.txt and breadcrumb.txt before guessing.
- A part given to a player goes through `ConstructEdits.Give` (hands, backpack, drop).

**Tools and input**
- Hold-to-repeat inputs never act on what the same stroke created (`SnapStroke`); try a stationary press, not only a sweep.
- Tool-mode state changes on transitions (taken out, put away), not per frame. A pick made before its tool arrives is kept for a bounded wait. A ring's marker reads the mode, and a tool's default option sits in the middle of its arc.
- The held-part ghost path is `Program.SandboxHeldOnConstruct`: a single-purpose part falls back to the crosshair's pick, then `SnapBuild.Nearest`. A refused aim always says why.
- Try a part-onto-part feature through the player's whole chain (menu, PlaceNew, loose / frozen / mounted, the client's ghost, the Attach message, the server's result) for hand-made AND generated parts. Sweep a "won't attach" report over every receiver and category, not one pair.
- A "don't move into X" check refuses only moves that make a NEW overlap and skips what the body is joined to. Try held bodies off centre, on the floor and coupled.
- `Keybinds.Migrate` unbinds every later-added action on its default before stepping versions.

**Parts, welds and drive**
- Several contact rays of one part are one wheel to the drive line: counting a Track part's rays as wheels on an open differential passed a tenth of the torque whenever one ray lost the ground, so a crawler crept at 2 m/s over rough ground (`TrackUnitsShare`). Try a drive change over bumps, not only on the flat.
- A drawn thing with thickness is in the contact height: a track's links hung a pad's thickness below the wheel's radius, so they sat in the ground and their inner face fought it (`CWheel.Band`). A hand check of ride height reads a settled, simulated body, not a spawned one.
- A contact the rim has refused is a wall, level: the cylinder's top edge threw a buggy up and onto a block just past the 0.6 R limit (`Rides`). Test the band just above a limit, not only well past it.
- A visual change of a part's size also changes its collider, player blocking, ray hit, outline and ghost checks: solid shape goes through `Construct.SolidCount/SolidBox/ShapeBounds`.
- Scaled sockets apply recursively to every part fitted on a scaled part, so try merges with a part that has its own sockets (a splitter), not only a barrel.
- A fixed-pose (freeform) segment is rigid with its parent for every system: ask `Construct.MoveSeg/Rigid/FrameOf`, never `Seg != 0`. Compare link points in the rest frame (`ToRest`); sockets meeting across a seam are `SeamLinks`.
- Welder and wrench share the leaf code (`ConstructBuild.OnLeaf/LeafRefusal/HangOnLeaf`). Try a weld path with every role whose behaviour depends on its links (wheels, suspensions, joints, drive line), driven, not only posed.
- A part's behaviour is gated on its own inputs and links, never on the construct being a vehicle (a seat, free, awake). An optimisation that skips idle bodies wakes them on an input or power change (`ConstructPhysics.EngineLive`).

**Network and merges**
- Prove networking fixes with separate peers over the real transport, with lag and loss.
- Anything written into a fixed budget in a fixed order takes turns (a cursor) and keeps room for its refresh; drive it past one packet (`ConstructNet.WritePoses`).
- A network-fed list with a cap starts at its full size (`new(Max + 1)`), or it allocates in a steady frame after a hitch.
- Bodies are drawn on the server's tick stamps (`ConstructNet.SampleAt`, `ConstructWorld.NetRenderTick`); the holder draws the aim plus a smoothed correction. Judge held and driven bodies by smoothness (second difference, back-steps), not final position. A buffer of past states covers the largest delay plus a sample either side, and a jump when new data arrives is eased (`ConstructNet.Smooth`).
- At a merge, read every flags byte and every `(byte)` cast by hand: a bit clash or a narrowing cast shows no text conflict.
- Per-match Player fields are marked `[MatchState]` and reset in `Player.ResetForMatch`; per-player state outside the Player is cleared in the mode module's `OnPlayerReset`, both via `Game.ResetPlayersForMatch`.

**Physics and data passes**
- For a solver instability, print each body's position, tilt and speed by tick before changing a number, change one thing at a time, and read anything a contact needs from the tick's velocities before the first impulse.
- When a physics change breaks a scene, first check whether the new result is what the shape should do; a scene puts a body where a real shape would move or fall.
- Stack blasts when trying digging. A nav graph finished over dug ground is flagged and dropped when the ground goes back (`NavGraph.BuiltOnDugGround`, `ForgetDug`).
- Measure a data-wide correction against the old shape before merging (`tools/props/zfight_moves.py`). Steps never grow with passes, the amount comes from the shared patch, and rotated boxes are not axis-aligned ones.

**When a test is asked for**
- An offline scene with bots spawns the local player through `SpawnLocalSafely`. A wall-clock loopback check waits for the state it needs with a deadline (`WaitFor`, `MatchWithin`), and frame loops never burst after a stall.
- An unknown part id in a test is `ConstructParts.All.Count`, never a fixed number. A scripted-clock fire test resets `NextFireTime` after giving a gun.
- A section that changes a shared map restores it in its `finally`. A check that passes filtered but fails in the full suite is reproduced with a manual full run (`gh workflow run build.yml --ref BRANCH`) plus a DIAG line, not by guessing at neighbours.

**Vehicles, wheels and joints** (folded 2026-11-09; dated stories in `git show c6b159b26:CLAUDE.md`)
- A wheel's forward sense comes from `Construct.DriveFwd` (`RollSign`, `RollAxis`), never `CWheel.Axle`. Try a turn's direction against the key, and a straight run's yaw after any tyre-force change (`HoldHeading`).
- A joint's motor gives back its gained motion through the whole body's inertia, never a holding force; per-joint maths over a chain is posed through it (`PoseCarried`), and chain tests compare against a hand moment.
- A part made for what goes on it is turned for that part (`SmartRotation.AxleUp`); try a feature from the ghost's own default on every face.
- Every generated vehicle has corners that hang alike (`VehicleChassis.Level`) and passes the spawn drive check (`VehicleDriveCheck`).

**Blueprints and bodies**
- A save records what is: steps' `"at"`, links' `"laid"`/`"hosts"`, `"exactLinks"`, a step's `"also"`, points relative to step 0. Round trips compare parts, joints, links and bodies, on selections of real vehicles too.
- Carrying parts across bodies remaps every joint index (`Hinge`, `Child`) and moves only what sits in the body frame (`FrameOf(seg) == 0`); try a join with a turned welded piece and read it back as a client.
- Points on a part go through `SegToWorld`; reach is one helper per action shared by client and server (`AttachReach`, `WeldReach`, `ReachPoint`).

**Riding, views and flight**
- Every way of leaving a moving thing is a ride-frame path (`ConstructRide.End`, or off with its surface velocity); state that decides how a replay runs arrives in the same message as the move.
- A world-side check on a construct is judged as the frame's mover would (any face up, a capsule on an edge); entry tests drop onto edges, beams and turned hulls, size tests use the biggest builds (`RideTall`).
- "What is under the crosshair" on a moving thing uses the picture just drawn (`PanelRender.DrawnSpot`).
- A view that replaces another has its own remembered mode and is tried with the other modes on.
- In flight, along the ground is `Along(v, up)` and up is `BodyUp`; try flight changes away from the world's +Y side.

**Input and UI**
- A wait for a key to come up reads the real key (`Keybinds.IsDown`); a press that opens a menu with a text box is spent until the key comes up (`_sbMenuKeyEaten`).
- Before taking a bit out of the input, find what reads it; a body's state is held at the body (`SandboxPlayer`).
- An ImGui button only notes its action; lists change after the drawing (`Ui.TwApply`), and an index is checked again after a change.
- Anything a machine holds is taken out through the player's own aim (`ConstructPress.MachineOf`).

**Performance and network**
- A per-tick method keeps its rare path and any lambda in a method of its own: C# makes the closure when the local's scope opens, before early returns (`AllocProfile.Top` finds them). An allocation measure's warm-up runs every stage the measured loop runs.
- A per-body history is checked against the body's structure before it is touched; network tests edit a body while it moves; a growing `[net] dropped` count is the first thing to read in a user's log.
- Everything drawn together is sampled at one tick; when something looks wrong on a client, check the host alone first.

**Checks and CI (when a test is asked for)**
- A wall-clock check waits for the state it needs with a deadline, judges medians, leaves out what the peer has no poses for, subtracts what a frame's time explains, and scales limits to speed; a wait's condition is false until the thing has happened; a client's player is moved through the server (`PwMoveClient`).
- Game code stamps tracers on `ConstructRender.Now`; a check picks its own events by what they are and measures a flight by its own ticks (`Age`). Pool slots past the count are stale.
- A check measures what the game does (e.g. `ConstructWeapons.AimWay`), not a proxy; a classifier is tried on a case for each class; a test's message says what it measured.
- A catch for an expected miss names what it expects; look a part up outside the try.
- When "SLOWEST" or DONE nears the workflow's limit (now 1800 s), raise it or trim sections.

**Helpers and libraries**
- A second program (the Browser helper) is rebuilt when its source changes; compare the built file's date with the commit before believing a fix failed. A probe runs through the shipped wrapper and settings (CefSharp starts with `--mute-audio`); anything crossing a process gets a throttled log line per hop.

**Generated geometry (hulls, lofted parts, vehicle dressing)**
- Hull faces keep only convex corners; a face is a repeat only when every point lies on the other's plane; facets meeting at a point share a mitred offset (`HullLoft.Mitres`); a new shape kind joins `ZFight.Boxes` when it is added.
- A part's place rounds to the centimetre and `Put` moves a part up to its reach (0.2 m) onto a socket: socket offsets are whole centimetres, floors in a row sit on a half-metre lattice (`Deck(..., aligned: true)`), stand-offs keep half a centimetre extra (`HullLoft.TrimGap`), and keep-clear tests take the placing call's reach as margin.
- A generated part keeps its own faces apart by construction: drawn feet a few mm above the rails' plane, helper boxes a few mm off drawn faces, pieces meeting at a shared point stop short of it, scaled sections drop rows too thin for their facets (`CabLook.Clamped`), thick pieces over a bend are flat with seams. `--body-report` counts coplanar pairs inside every lofted part.
- Dressing goes on with `Onto(..., clear: true)`; turret and gear spots come from the bays as built (`BaySpots`); a refusal names its blocker in the rest frame (`Vehicle.Blocker`); judge shapes on real meshes (`--plan`, `--vehicle-shots`) and `VehicleChecks.Gaps`.

**Added after the fold**
- 2026-11-09: after fix/zombie-lod a Sandbox client still drew a far horde the host didn't have, which no shot could hurt: the zombies' sends stopped with the last zombie (no empty cells went out) and a full Msg.ZCrowd always restarted at cell 0, starving the last rows of a big crowd. A delta stream sends its final empty state and resumes where a full message stopped; measure what the client holds against the server's counts (`--zrange-host`, `CrowdStale`).
- 2026-11-10: after the gunsmith merge every headless run failed or hung in `World`'s static initializer: a shell gun built while the blueprints loaded read `World.GravityScale`, the initializer loaded the maps, a sandbox map looked up a blueprint and re-entered the blueprints' Lazy. A static initializer never loads data that can lead back to its caller: take it on first use (`World.Map`).
- 2026-11-10: from the Ordnance Bench's merge (65580f2a) Nudge online, Point weld, Press stock and Custom bins failed in the full suite only: the Interaction sweep opened the bench, and neither `SandboxLocalReset` nor Esc closed it, so `MenusOpen` ate every later click. A new panel that holds the mouse joins `SandboxLocalReset` and the Esc chain in the same change.
- 2026-11-10: after the ordnance cook-offs (b3da5279) Terrain streaming saw a broken bomb never go off: it now goes off as a landed shell, and the check counted prop blasts only. A change that moves an effect to a new path updates the checks that count the old one (`ShellsLanded`).
- 2026-11-10: fix/turret-seat ran the rotary joints' speed from stopped (min 0) and Construct actuators still wanted a minimum above zero; a range change in a part generator is followed by the checks that read that range.
- 2026-11-10: two `ImGui.TextColored` calls in Lobby.cs failed the source scan for printf text; text that isn't a plain u8 literal goes through `ImGuiSafe`.
- 2026-11-10: with 281 shipped blueprints the parts menu, Construct configure and Refining network spawned the wrong blueprint or none: `CActKind.Blueprint` sent its index in one byte. An index into a list that grows with data goes on the wire in two bytes, and a test's "bad index" is past the wire range, not a fixed 255.
- 2026-11-10: the 6587-part TitanWalkerFitted blueprint made Sandbox full hands rebuild it once per part (43 million part builds) and the full suite hit the CI timeout. A check that loops over every blueprint costs by its size: sample the giant ones (`n * n` capped).
- 2026-11-10: event doors (plain "hang" socket, role door) had no usable snap point, since SnapBuild made every door single-purpose by its hinge leaf; Sandbox snap build caught it on main. A part family with a new way to attach is classified in `SnapBuild.Build` in the same change (an event door goes on by its hang only).
- 2026-11-10: a Titan Walker lying on Caledon made Sandbox ticks 15 ms (F9 "Player moves", which times the whole `Constructs.Step`): `PlanetGround` asked every leaf of a big group for its own patch of ground, so a 128x64 m slab tested 736 triangles and a fitted Titan made 5,300 ground queries a tick. Per-tick work against the world is bounded per body whatever its size: the lowest pieces kept from tick to tick plus a slice of the rest, a shared triangle budget (`PickGroundLeaves`, `MaxGroundTris`); measure with the biggest blueprint lying on a slope, not standing on the flat.
- 2026-11-10: the smooth tops (PR #91) were judged on single craters and drill holes; in play, blasts on the floor of earlier craters left overhanging lips (blocks and spikes round the rim), sides steeper than 2 m a column stayed bars, and a hole's side stopped square at the untouched cell beside it. The "stack blasts when trying digging" lesson covers looks too: judge dug ground on a dozen blasts in one place and on a very big crater with small ones inside, from the ground and from above (`TerrainEdits.CraterTop`, `TouchBeside`, `GroundTops.Near`).
- 2026-11-10: with the suite cut into four parts, Ragdolls and knockback ran straight after Nav bake, which ended on Pocket Planet: its gravity threw the thin-wall body over the wall. The rule above again (a section that changes the shared map puts it back in its `finally`); a section that fails in its part but passes alone is bisected with `--selftest-only "Earlier, Failing"` pairs.
- 2026-11-10: from the shared materials (a3872067) to fix/zombie-far-horde no crowd zombie was drawn (only the 300 individuals): the box FragmentSrc gained `vLocal`/`vLocalN`/`vSize` inputs, `Renderer.Crowd.cs` reuses it without writing them, the link failed once into the log and the F9 counters still counted the prepared instances. A shader that reuses another's fragment code is updated with its inputs in the same change (grep `FragmentSrc.Replace`), and a link failure logs the info log.
- 2026-11-11: after the survival physics gun went to 5,000 kg (7a10c317) "physics gun feel" failed on main: its 450 kg "heavy" piece was no longer limited by the beam, so it moved as fast as the 100 kg one. A check that reads a limit takes its amounts from the constant (`SurvivalMaxMass * 0.3f`), not a number picked against the old value.
