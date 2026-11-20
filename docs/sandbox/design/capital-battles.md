# Capital battles: bores, break-up, scrap

Karl (2026-11-20): capital ships should fight with the damage visible (a railgun shot through the ship, many penetrations with
several capital ships fighting, a ship breaking up) and no loss of frame rate. Background: the holes study (Claude Doc
PN79Syp1w9HwMJzfpEipQU) and its spike, draft PR 201 (not for merging). Test ship: the Mainframe, archived 2026-11-20 (docs/archive/capital/mainframe.md); `--battle-check` now defaults to the Zero Day.

## What is in (phase 1)

- **Bores** (`Source/Constructs/ConstructBores.cs`). A penetrating gun with a `"pierce"` budget in its part file (the Zero Day
  Railgun: 3000) no longer stops after `MaxPierce` (3) parts. Its slug goes on through part after part, each costing
  `CostBase` (40) plus armour x metres crossed x `CostArmour` (12), up to `MaxParts` (96). Its damage falls with the budget
  left, to `FloorShare` (35%). When nothing is within `MaxGap` (400 m) ahead, what is left flies on as a flying round
  (`CRound.Pierce`, ConstructRounds.cs) and bores into the next ship it meets. Guns without a budget keep the old rule.
- **Spall.** At each of a shot's first `MaxSpalls` (8) exits, three short rays in a 26 degree cone (5 m) each hurt the first
  part they meet with 12% of the round's damage.
- **The holes, on every PC.** The server sends one `CFireKind.Bore` a shot: where it went in and where it last came out, and
  its calibre (protocol 384). Each PC marks the holes on its own copy (`ConstructBores.Mark`): a hole on each face it went in
  and out of, kept in the part's segment frame (hull plates are often on a welded segment of their own) and found again by
  the part's position when the parts are renumbered (`ConstructWorld.BoreMarkLive`). Drawn by `Program.SandboxBores.cs`: a
  dark scorch octagon 6 mm proud of the face, its rim glowing white-hot to orange to dull red over 30 s, between the face
  and the scorch so only the rim shows; plus sparks at each exit and a short hot streak along the way through. At most 4,096
  holes at once (the oldest go), drawn to 900 m. Looks only: not saved, no shape changes (HullHoles stays for person-sized
  breaches).
- **Break-up** (`Source/Constructs/ConstructBreakUp.cs`). A body of 1,024 parts or more keeps, across its long axis every 8 m,
  each plane's strength: the parts spanning it and the links crossing it, each by the health of a 4 m2 patch of it (a 60 m
  hull plate's whole health, often 400,000 to 1,000,000, would never give way to a hole), less the damage that landed in
  the slice either side (`ConstructWorld.DamageAt`, set by bores, spall and blasts). The first hit takes the base. When a
  plane is down to 35% with 160 parts or more each side, the body splits there: the far side from the root goes as a wreck,
  the halves are pushed apart at 0.6 m/s with a slow tumble, and six blasts go off along the break over 1.5 s. What is left
  starts a new base. Looked at only after a hit, every 6 ticks, O(parts + links); one snap a tick.
- **Scrap.** A part under 4 t shot off a body that big, and a piece of up to 8 light parts it leaves hanging, no longer
  become wreck bodies: they burst into pieces drawn only (`CFireKind.Scrap`, the prop debris pool). On the Mainframe, 150
  hits took the stray bodies from 127 to 20.
- **Rebuild.** `Construct.BuildSeamLinks` tested every pair of parts (16 million on the Mainframe, 67 of a full rebuild's
  72 ms in the cloud); it now sweeps along the longest axis and makes each part's free sockets once (55 ms). The seam links
  are the same on all 378 shipped blueprints (`--battle-check --seam-hashes`, compared before and after).

## Trying it

- `--battle-check [SHIP] [--shots N] [--per-tick K] [--range M] [--focus F] [--blasts N [--blast-damage D] [--blast-radius R]] [--old-pierce] [--no-break-up]`
- `--battle-check SHIP --idle --ground [--map NAME]`: the ship left resting on the ground, 12 rounds of 60 ticks: the world step, the physics' parts and whether it sleeps (an idle ship should sleep within a few seconds and cost next to nothing).
  (headless, `Source/DevTools/BattleCheck.cs`, output in callofslop.log): the ship (default the Mainframe) held in the air on
  the Proving Ground, N Zero Day Railgun shots at its broadside, K a tick, each volley's cost and what it did, and at the end
  the bodies left by size. `BORE_TRACE=1` prints every part each bore went through and each break-up look.
- Window verb `bore PART[#n] [N] [x|-x|y|-y|z|-z]` (host): N slugs at a part from 80 m out on that side of its body, through
  the server's own bore.

## Numbers (cloud box, 2 cores; Karl's PC is faster)

Mainframe, 150 shots 3 a tick at one section (`--focus 0.1 --range 60`): 1.2 ms a shot for the bore and its spall, 0.2 ms a
shot for the holes on a PC; about 4.5 parts a shot (the slug goes in through the armour and skin, crosses the open interior
and out the other side); it snapped at 616 m after about 120 hits there. Spikes left: the tick a part breaks still rebuilds
the whole ship (55 ms in the cloud) and its collider (10 ms), and the tick it snaps builds the wreck half (30-40 ms).

## Next

- Collider and rebuild per 32 m chunk (the journal skips any body with freeform welds, and the Mainframe's plates are welded).
- Far hits recorded and turned into geometry only when someone is near; impostors past 3 km (the renderer thread's).
- Bores through ships out of the bubble (they are posed, so a round meets them only inside it).
- A joined client's holes were not tried over the wire; nor a save and load.
