# Time speed to 1,000,000x and steady drawn orbits (2026-11-17)

Notes for the warp branch (`claude/smooth-warp`). The rules are in SPACE.md "Time speed" and COMMAND.md "Time speed".

## Why it stopped near 15,000x

`SpaceShips.MaxSpeed`: a coasting ship takes at most 64 substeps a tick, each at most 1/48 of its orbit. A ship in a
low orbit of a small world (Glacier, Cairn, Crumb) has an orbit of minutes, so 64 x 4 s x 60 ticks = 15,360x. Those
ships never went on rails because the rail tolerance (10^-6 of the primary's pull) is under a moon's tide (Slopolis on
Glacier: about 6 x 10^-6). The clock's own cap and the setting were 100,000x.

## What changed

- `SpaceClock.MaxSpeed` 1,000,000 (command view button "1M", Settings "Fastest time speed" choice and default
  1000000x; an old settings file keeps its 100000x and the clock line shows "SETTINGS' LIMIT"). Past 250 years of
  space time the clock runs at 1x (`SpaceClock.LatestNs`: `SpaceTime` ends at 292 years, about 2.5 hours at 1M).
- Above 10,000x (`SpaceTimeRule.WarpRailsAbove`, worked out from the speed the rule would allow with the game's
  limits and nobody burning) `SpaceShips.WarpRails` lets a coasting ship take rails under 2% perturbation. At 10K and
  under nothing changes. Away systems follow the bubble's clock and its rails.
- Rails at high speed: `Advance` cuts a tick into up to `MaxRailChunks` (256) looks for rail ships, each at most half
  the time to the nearest sphere of influence it could reach (its own edge if its orbit gets there, a child's whose
  orbit band its own crosses); the speed limit counts it. One look is a Kepler solve per ship and per body.
- `SpaceShips.Sky` per body (the game sets the bubble world's sky's edge and each landable world's,
  `Game.SpaceSkies`): a coasting ship whose periapsis is under it holds the speed so it takes four ticks to get there.
- Burns stay real time (unchanged): any thrust holds 1x, ordered ships slow it before a burn (`Game.NavSpeedCap`).
  Only the host sets the speed, for everyone (unchanged).

## The jittery drawn orbits

The command view draws each craft's conic from `Game.NavState(c, frame time)`. On the host that was the ship's state
at the last tick (the construct's pose, or `SpaceShips.PositionOf`), while the bodies were drawn at the frame's time,
which runs up to a whole tick ahead (half an hour at 100K): the offset from the planet was off by the planet's motion
and spin over that time, so the conic was a different one every frame. Now the host carries the ship's two-body orbit
to the frame's time (`SpaceShips.StateAt`, from its rail's epoch when on one), as a client's `ShipTrack` already did.
The other part was real: a coasting ship's osculating conic moves with a moon's tide, many of the moon's orbits a
second at high speed; on rails above 10K it holds still.

## Checked

- `--space-ships Systems/Frostreach.json --hours 2400 --speed 1000000`: 1,000,000x from the second tick on, every
  orbit on rails, energy round each primary constant to the digit from then on; 20 years (`--hours 175000`) the same,
  42 us a tick. At 100K and earlier code: 15,360x.
- `--window-shot ... orbit` (the picked craft's drawn conic to the metre, per frame): see the branch report.

## Left

- A ship on rails ignores a moon's tide that would, over months, change its orbit (Rime's at 1.5 radii escaped at
  15K; on rails it stays). That is the patched-conic trade.
- `--space-ships` on Dev.json at 10,000x for 2400 h logs failed drifts from the moonlet ship's decaying orbit
  (adaptive, unchanged by this branch); not compared with main.
