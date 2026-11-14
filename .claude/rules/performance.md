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
