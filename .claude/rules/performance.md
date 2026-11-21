---
paths:
  - "Source/Perf/**"
  - "Source/Constructs/**"
  - "Source/Game/**"
  - "Source/Physics/**"
  - "Source/Render/**"
  - "docs/perf/**"
---

# Performance: the limits and what broke them before

Sandbox scale (many constructs, parts and links, big worlds) is what these protect. Add a new line at the end: date, what went wrong, why, the rule.

- No server tick may do more than 8 ms of CPU work (one-off JIT excluded). No per-frame or per-tick allocations: the self-tests measure them. Music CPU stays at or under 1.5x the baseline.
- A per-tick method keeps its rare path and any lambda in a method of its own: C# makes the closure when the local's scope opens, before early returns (`AllocProfile.Top` finds them). An allocation measure's warm-up runs every stage the measured loop runs.
- A network-fed list with a cap starts at its full size (`new(Max + 1)`), or it allocates in a steady frame after a hitch.
- Per-tick work against the world is bounded per body whatever its size: the lowest pieces kept from tick to tick plus a slice of the rest, a shared budget (`PickGroundLeaves`, `MaxGroundTris`). Measure with the biggest blueprint (the fitted Titan Walker) lying on a slope.
- Time a tick spike down to the per-part call before guessing at its source: F9 "Player moves" times the whole `Constructs.Step`, and a collider rebuild inside it was billed there (2026-11-11).
- A check that loops over every part, blueprint or map costs by its size: sample the giant ones (2026-11-10: a 6587-part blueprint rebuilt once per part, 43 million part builds, hit the CI timeout).
- An optimisation that skips idle bodies wakes them on an input or power change (`ConstructPhysics.EngineLive`).
- A per-tick scan over every body keeps nothing for a body it has no business with: the lifters' scan (every second in every world since arms and cranes phase 4) made a record for each new shell or wreck piece and grew its dictionary, 80 B to 2 KB in a world step (2026-11-13: "Construct gun recoil" and "Construct actuators" red on main). Keep the look's stamp on the `Construct` (`LiftStamp`) and a record only where there is something to hold.
- 2026-11-18: main went red on "Sandbox tool models" (98 KB over 240 frames) after the Mining Shell added a node level and a kit to every player rig: `Scene.Enqueue` queued a node again each time a draw cleaned it and a pose dirtied it, and compaction kept every duplicate of a node that was dirty again, so the queue doubled without end once a rig dirtied enough nodes a frame. A node is in a scene's dirty queue once at most (`SceneNode._queuedIn`); a rig change runs "Sandbox tool models" by name.
- 2026-11-18: on a Zero Day in orbit, a torpedo taken off its rack (then the rack) took the game to 1-3 fps: the contact margin between two bodies was the SUM of each one's speed times dt, and a posed ship out of the bubble counted as still, so a loose piece aboard at 207 m/s had a 3.5 m margin and every piece of the five torpedoes beside it went through the hull-hull test (320 pairs, 11 ms a tick). Contact reach between bodies goes by their closing speed, with a posed ship's own velocity as the solver's `VelSide` takes it (`GroupMotion` posedMoves); time a change to contacts with loose parts aboard a ship in orbit, not only on the ground.
- 2026-11-20: a resting Mainframe on Caledon cost 1.3 ms of physics a tick (5 ms on Karl's PC) for minutes: its 33 Engineer's Panel charge dials stepped a hundredth as the batteries charged, and every logic output change woke the body, so it never slept and its ground contacts ran every tick. A readout's output (gauge, instrument, display: `CLogicKinds.IsReadout`) never wakes a body; time an idle big ship with `--battle-check NAME --idle --ground --map Caledon` (sleeps by round 2).
- 2026-11-21: Hexkey phase 5 (PR 427) turned "construct salvage", "construct storage" and "Frame allocations" red with 64 B a tick: `SandboxHands.StoreMoved = StoreShopMoved` in the per-tick rules made a new delegate every call. A method group handed to a hook in a method that runs every tick is cached in a field (`_sbStoreMoved ??= ...`), as its neighbours are.
