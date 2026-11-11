# Weapon builder: the Zombies economy (archived)

Sections of docs/sandbox/WEAPON-BUILDER.md that are only about the archived Zombies mode's economy, moved verbatim on 2026-11-11. The weapon builder itself is live: parts, rules, stats and models stay there.

## How it fits the Zombies loop

- **Finding parts.** Zombies drop parts (later rounds roll rarer tiers), salvage spots on the map give a part once a
  round, and the slot machines pay them (there is no scrap since 2026-10-27: "the gambling machines replace the need").
- **Workbenches** on the map are where you assemble, swap and take apart builds.
- **Mystery box** gives a *standard complete configuration* only (decided): a whole build from `Parts/Builds` (for
  example the Minigun or the Twin Splitter Shotgun), never a single part. You can then take it to a bench and change it.
- **Wall weapons** are plain, working guns (one grip, one receiver): the standard builds marked `"wall"`. Bought off
  the wall, they are builds too, so you can modify them at a bench.
- **Weapon slots.** Players carry **2 builds**. An upgrade bought on the map (the "Mule Kick" of this game, proposed
  price 4000) raises it to **4** for the rest of the run.
- **A roguelite run.** You start each run with low-level gear (the `"start"` builds: a wall rifle, a wall pistol, a
  machete) and climb, inside one run, to broken, overpowered, silly guns. Bad parts along the way can make it hard.

## Weight and run progression (proposed, open for the user)

Weight stays the main limiter (decided). A grip carries up to twice its carry weight, and the build gets slower and
slower to aim and move past its carry weight. Bigger, rarer frames carry more and hold more receivers, and
weight-relief parts (`"carry"`) raise what any grip carries, so the huge guns are a **late-run** thing:

| Rounds (proposed) | What drops (part tiers) | Frames you can expect | What a build looks like |
| --- | --- | --- | --- |
| 1-3 | common 85%, uncommon 15% | Service Pistol Grip (4 kg), Service Rifle Grip (8 kg), Sport Rifle Grip (10 kg): **one receiver each** | the start builds: Wall Rifle, Wall Pistol, a Machete |
| 4-9 | + rare 8% | + Double-Tap (2), Pistol Grip (3), Quad / Penta / Hex Rack (4-6), Briefcase | the first two- to six-receiver guns, a Y-splitter shotgun, first upgrades |
| 10-17 | rare 24%, epic 6% | + **Mule Frame (16 kg, 6)**, Shoulder Harness, then the epic frames: **Exo Harness (30 kg, 12)**, Septet / Octet Rack (7, 8), Rifle Grip (9), Steering Wheel | three to twelve receivers, a Manifold Hopper, Gas Overclock, Rail Accelerator |
| 18-24 | epic 22%, legendary 5% | + **Gantry Rig (48 kg, 16)**, **Titan Harness (45 kg, 12)**, Anti-Grav Coil | the legendary parts: Bunker Buster, Tripler Chamber, Doom Hammer, Depleted Core, Conga Drum |
| 25+ | legendary 18-30%, wonder 5-13% | + **Parade Float (60 kg, 16)**, the Push Cart, Jetpack Stock | broken, silly, sixteen receivers: Parade Cannons on a Five-Alarm Chamber, the Clown Car Hopper, the All-Seeing Eye |

The numbers live in `Parts/Tables/DropTables.json` (format `call-of-slop-drops/1`): part tiers by round, the chance a
zombie drops a part by round (4% -> 10%), the mystery box's build tier by round, and prices (box 950, slot upgrade 4000,
wall weapon 500), and what a **slot machine** pull pays by round (`slotTiers`: nothing, points back or a
loose part of a tier; ZOMBIES-MODE.md, "Slot machines": the slots are how a run gambles for single parts, since the box
gives complete builds only). `RunTables.Roll(rows, round, seed)` rolls deterministically (the same seed gives the same tier on
the server and every client). Nothing rolls them yet: the Zombies mode will. Bad parts along the way (heavy, slow,
drawbacks on multipliers) are what make a run hard.

## Weapon slots: 2 -> 4 (decided: an on-map upgrade, per run)

- The game has **four weapon slots** now. Slots 3 and 4 only open once the player's run flag `ExtraSlots` is set
  (server-owned, sent to that client in `RunState`); otherwise everything behaves exactly as before (2 slots).
- Keys: **3** = slot 3, slot 4 is unbound by default (bind it in the settings), and the swap key and mouse wheel cycle
  through every slot you have. The HUD lists your other slots.
- **Workbench Range:** a station stands by the spawn line (right side); **F** there unlocks slots 3-4 (free in the
  sandbox; proposed price 4000 points in Zombies). The workbench has a toggle to start with them unlocked; slots 3-4
  take the workbench slots that aren't the selected or second one.

## The push cart (a real object)

The Push Cart grip is now a life-size shopping cart (bar at about waist height, sixteen receivers in the basket).
When you hold a cart build, a **cart entity** exists in the world (server-authoritative, `GameZombies.cs`; physics in
`PushCart.cs`):

- **Pushed.** Every tick the cart turns toward where you face (2.2 rad/s: carts turn slowly) and rolls to a spot in
  front of your feet (up to 7 m/s), colliding with the world as a box. It **rolls over curbs up to 15 cm**, but **not
  higher steps** and **not slopes steeper than 22 degrees** (front wheels against back wheels). When it's stuck it
  slides along the wall if it can; otherwise **you are held at the push bar** (you can't walk through your own cart).
  It's slow to push because a cart build's mobility is low (the frame's 0.65 times the weight penalty).
- **Hands on the bar.** In first person the cart isn't a held gun: it's drawn where it really is on the ground (its
  world pose in camera space) and both arms IK onto the push bar; it rattles a little as it rolls. Your eyes are well
  above the basket, so the view swings the cart up about the eye until its basket is no more than 17 degrees below the
  middle of the screen (the hands follow the bar in from the bottom). **It doesn't fire
  while you push it** (decided with the manned gun: both hands are on the bar, and it keeps "park it, then man it" the
  one way to use its firepower). Pulling the trigger tells you how.
- **Parked (F while pushing).** The cart stays where it is and you go back to the weapon you held before. It does
  nothing on its own. Switching weapons while pushing parks it the same way. A dead owner's cart is gone (it comes
  back in front of you on respawn).
- **Manned (tap F at a parked cart's bar).** Decided by the user: **a gun you fire yourself, not a turret**. You stand
  at the push bar (your feet are held there; the movement keys do nothing) with your hands on its handles, and the
  cart body stays put. The view sits low behind the gun (just over the push bar, a step back) so the gun in the basket
  is in view, and your shots leave from there. The mouse turns its gun (everything mounted on the cart: the receivers
  and feeds in the basket turn as one block about their middle) up to **60 degrees either side** of the cart's facing,
  **15 degrees down and 30 up** (proposed); your view is held inside those limits. The fire key fires the cart's build
  through the normal server-authoritative shot path with the same stats (damage, fire rate, spread, rays per
  receiver), reload works as usual, and the HUD shows the cart gun's ammo and "Leave: F   Push it: hold F". The
  server clamps every shot's direction to the traverse limits too.
- **Leaving.** Tap F again, or jump or sprint: you step off and get the weapon you held before back. **Hold F**
  (0.4 s) at a parked or manned cart to push it again; tap F stays "man it / step off". Selecting the cart's slot next
  to it also picks it up; far away you're told where it is.
- **An auto turret may come later as a separate deployable.** The turret behaviour (auto-targeting, 30 s, two
  magazines) is removed from the cart entirely; nothing of it is left in the code.
- **Network:** the server sends every cart in `CartState` with the snapshots while any exist; the interact key is
  `CartUse` (see "Firing" for the protocol notes and what co-op still needs).
- **Workbench Range:** a cart lane on the left of the spawn (x -12): an 8 cm curb the cart rolls over and a 30 cm step
  it can't climb. The workbench's "Practice dummies" setting (0-8, default 4) puts dummies down the range for the
  cart gun, cleave and stun.
