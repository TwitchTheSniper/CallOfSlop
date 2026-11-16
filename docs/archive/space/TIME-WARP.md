# The time warp (archived 2026-11-16)

**Superseded.** Karl, 2026-11-16: "warp" no longer changes the flow of time. Ships coast between planets in real time,
and the host speeds time up from the command view (docs/space/COMMAND.md "Time speed" and "Between planets"). The
clock's multiplier is now called the **time speed** everywhere: `SpaceClock.Speed` (was `Warp`), `SpaceTimeRule` (was
`SpaceWarpRule`), `SpaceFlight.RequestSpeed` / `RequestedSpeed`, `SpaceShips.MaxSpeed` (was `MaxWarp`),
`Game.RequestTimeSpeed` (was `RequestSpaceWarp`), the world save's `SpaceSpeed`, `--space-ships --speed`, and the window
tool's `speed N`. The F7 Space window's warp buttons are gone: the buttons are in the command view's top left.

What follows is the text docs/space/SPACE.md held about the time warp before then, kept for the history. Where it
says warp, read time speed; the rule itself (host only, 1x while a ship in space burns, never past what the ships can
follow) still holds.

---

## From "Ships (phase 2)"

**Warp.** `SpaceShips.MaxWarp` is the fastest warp at which every ship still takes its full steps within a per-tick
budget (64 coasting substeps or 16 adaptive steps a tick); ships on rails don't limit it. The warp rule
(`SpaceWarpRule.Effective`) is the architecture's **provisional** proposal, not yet confirmed by Karl: only the host sets
the warp; above 1x only while nobody is busy in a bubble (`SpaceFlight.AnyoneBusy`, set by the game from phase 3); never
past `MaxWarp`; the host may always pause (0).

## From "The bubble (phase 3)"

- **Warp.** The host asks for a warp (`Game.RequestSpaceWarp`, or F7 > Space); `SpaceWarpRule.Effective` is still the
  provisional rule. "Busy" (`Game.SpaceAnyoneBusy`) is only a ship in space burning its engines (its thrust needs real-time
  steps). Life in the bubble (walking, driving, building, anything awake) doesn't count: the bubble's physics runs at 60 Hz
  whatever the space clock does, so it never stops the orbits (Karl, 2026-11-04: until then anyone moving dropped the
  warp to 1x, and the sky and orbits seemed to stop).
- **The Space window** (F7, the Space button): the system and body, the space time and warp, the sun's elevation where you
  stand, the moon, the ships; the host's warp buttons.

## From "Leaving the bubble"

- **Warp.** A driver who only coasts isn't busy, so the host's warp runs in orbit; a burn holds it at 1x.

## From "Next phases"

3. Done (above, "The bubble", and "Leaving the bubble"), apart from moving the World's origin. Travel is decided now (Karl,
   2026-11-03): players build ships that fly, so leaving a bubble means a built ship becoming a phase 2 ship. Time warp
   with friends is shelved: the host-only provisional rule stays.

## Elsewhere

- docs/space/FREE-TRAVEL.md (2026-11-05): "No time warp, so every world shares one clock." (Every world still shares one clock; it is the time speed that the host sets.)
- docs/space/GALAXY.md: a jump arrives in orbit round a world "since there is no time warp to cross half an AU with"; docs/planets/WORLDS.md: Redrock "a long flight with no time warp".
