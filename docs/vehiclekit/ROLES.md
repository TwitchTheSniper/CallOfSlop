# Vehicle kit: roles, families and cargo

Kit 4's roles and tiers, families and convoys, cargo and spares. Part of the vehicle kit guide: index in VEHICLEKIT.md.

## Roles and tiers

Kit 3 makes a faction's vehicle by its shape: ask for a Box Truck and you get a box truck, whatever it is for. **Kit 4** (`VehicleKitVersion.Roles`, `VehicleRoles.cs`) adds what a
vehicle is *for*: a **role**, stated in needs the built vehicle is measured against, while the style still decides how it looks; and a **tier**, the threat ladder's rung within the
faction (zombies 1, bandits 2, military 3, high tech 4 as a place's danger; a Bandit tier 4 is a warlord's truck, still a Bandit's). Harder places hold harder enemies and better
loot, and players salvage what survives.

**Which kit.** Only a vehicle asked for by role is kit 4. Every request by shape, every committed seed and every tool run without a role is kit 3 (or kit 1) exactly as before, so
`--gen-vehicles` still reports all 123 identical and `Current` stays kit 3. `--kit 3` on a role run makes the seed without its role and measures it against the role all the same,
which is how the numbers below compare the two.

**The roles** (`VehicleRoles.All`; needs at tier 2; the speed is the cheap estimate below):

| Role | For | Shapes (weight) | Needs | Leans |
| --- | --- | --- | --- | --- |
| scout | fast, light, 1-2 crew, one light gun, little loot | Technical | seats up to 3, 1 gun up to medium, 27+ m/s, loot up to 3, up to 7 m | one axle |
| raider | fast to middling, 2-4 crew, guns, some loot | Technical 3, Box Truck 2, Gun Wagon 2 | seats 2-4, guns 1-3, 22+ m/s, loot 1+, up to 9.5 m | gun beds, rams, short |
| gun platform | slow, heavy gun(s), a deck | Flatbed Fort 3, Gun Wagon 3 | 2+ guns, medium+, an open deck | gun beds, hutches, gun decks, walled sides |
| carrier | an APC: crew space, armour, little gun | War Bus 3, Box Truck 2, Double Decker 2 | 3+ seats, up to 2 guns, armour 11+, a room | rooms, long, armour |
| tanker | fuel or cargo, weak points exposed, rich loot | Scrap Tanker 4, Tanker Rig 2 | up to 2 guns, 400+ l of fuel, its fuel in the open, loot 1+ | tanks, a chest on the deck |
| recovery | crane, winch or tow, little gun | Box Truck 2, Flatbed Fort 1, Gun Wagon 1 | up to 2 guns, a tow hitch, a winch | hitches, beds |
| command | masts and radio, armour, one gun, the best loot | War Bus 2, Box Truck 2, Double Decker 1 | 1-2 guns, armour 11+, a room, 2+ signals | rooms, roof racks, armoured cabs |
| boss | the largest: most guns and armour, the best loot; rare | War Bus 3, Flatbed Fort 1, Double Decker 1 | 3+ guns, armour 11.5+, 8.5 m+, a room | long, gun beds and decks, armour |

A role names only the shapes that can meet it: the Box Truck was a scout in the first draft, and measured 20 to 25 m/s against a Technical's 25 to 34, so no grammar shape is a
scout. A style fields only the shapes it builds (the British style's Double Decker is an extra the others don't ship).

**The measure** (`Vehicle.MeasureRole`, `RoleFit`): seats (every seat part), guns (turrets) and the heaviest gun's class (`VehicleRoles.ClassOf`, by the receiver's bore and action:
carbines and pump guns light, full-auto rifles medium, harpoons and flame guns heavy), the **estimated top speed** (the engine's peak power, 60 % of it at the wheels, against
rolling resistance 0.03 and drag Cd 0.9 over the frontal box: it ranks vehicles and runs nothing; a Bandit Technical estimates 31 and drives 24 in the check's 6 s), mass, the
**armour rating** (the mean armour of the body's panels by area: sheet steel 6, hull bays 8, tread plate 10, plate steel 20, armour plate 40), loot (containers that roll
from a table plus loose cargo pieces) and its table's tier, the length, fuel carried, and whether it has a room, an open deck (a bed or a porch), a tow hitch, a winch, its fuel
in the open (the identity score's fuel weak point, there and hittable) and how many signals (the style's `mast` parts, else its trophies).

**Tiers** (`RoleAsk`) move the needs and lean the choices:

- guns: a fighting role (raider, gun platform, boss) may carry one more from tier 3, rolls one more at tier 4 where its shape has room (a lean, not a need: a Technical carries one; needing it failed 41 of 144 tier 4 vehicles), and needs at least a medium gun from tier 3; an armoured fighting
  role (the boss) wants a heavy one at tier 4. At tier 1 nothing heavier than medium (no harpoon, no flame gun). A style with no heavy gun (IT, the Call Centre, Militia)
  fields its heaviest. Guns weigh by class and rarity: a rarer receiver (the Whaler, the Firebreather, Militia's battle rifle) is likelier from tier 3, less likely at tier 1.
- armour: the needs rise half a point a tier (a whole point failed 18 of 144 at tier 4: appliqué and plates reach 12 to 14); the plates lean further to armour (+0.15 a tier).
- size: segment lengths lean longer, 0.15 a tier.
- loot: the role's loot tier is its base plus the tier less one (scout, raider, gun platform, carrier and recovery 1, tanker 2, command and boss 3), to 4; tiers 1 and 2 roll
  the style's own table, 3 and 4 its richer ones (`"bandit 3"`, `"bandit 4"` in `Constructs/Loot.json`: two to five rolls, rarely empty, circuit boards, control units,
  seekers, a jetpack). A tier 4 vehicle also tries one more chest.
- part quality: running gear and the light cab weigh by the tier of their recipe (`FactionStyle.Weigh`): among a style's own engines a tier 1 Technical leans to the Engine,
  a tier 4 one to the V8. Heavy vehicles' slots list one engine in every style, so for them the tier shows in armour, guns, size and loot.

**How a role leans the grammar.** Its module factors multiply the style's (`VehicleRole.Modules`, body patterns by their segments'), its length lean adds to the style's, the
chance an open bed carries a gun is its own (a gun platform 1, a carrier 0.2), the gun count is rolled within its needs' range clipped to the recipe's, guns are picked by
class and rarity, and the chassis table leans by its speed (a fast role weighs one axle up, a slow or armoured one a second axle). Armour leans the cab (armoured, enclosed),
walled bed sides and loaded flanks, and the plates: `FactionStyle.Weigh`, a per-thread hook that is null for every other vehicle, weighs the style's own plate materials by
their armour (only its own: an Armour Plate deck or wall, tried first, stands thicker than any a style lists and refused a Flatbed Fort's bunker brow beside it; the armour plate goes on as appliqué instead, below), so the identity stays the style's.

**The role pass** (`Vehicle.RoleKit`, after the marks pass, on forks of the kit layer: "kit role winch", "kit role armour", "kit role signals", "kit role chests"):

- a recovery vehicle's Cargo Winch on a free up-facing socket of its porch, bed, rack, a tank or the tail rails, clear of every turret's sweep and under open sky;
- **appliqué armour** for a role that leans to armour (0.3 and up): the style's heaviest armour plate, a metre square, welded flat over the sides of its rooms' hull bays a few
  centimetres proud of each bay's widest point (no shared face, nothing inside the room), never on the door's side of the door bay, a metre apart and more, only where it meets
  nothing (panniers, sponsons, the ladder, wheels), up to half the bays' sides, in the style's body colours. Hull bays are armour 8, so without it a room's armour hardly moved
  (carriers 10.0 by shape, 10.8 leaned);
- a command vehicle's signals on the marks pass's spots (the roof, the cab, a post);
- the role's extra chests: against a room's end wall, or for a vehicle with no room (a tanker), on its deck in the open;
- every loot container's table set to the role's loot tier (`BlueprintScript.RetableLoot`).
- its own cargo: ammo for its guns, spares from its own build and spare guns, stowed as a crew would ("Cargo and spares").

**Re-plan.** Each plan that builds is measured and scored for its faction; a plan that misses the role, or has a mark missing or an identity under the style's threshold,
plans again, up to three built plans (`VehicleRoleMake.Plans`; plans the game refuses as it builds don't count, up to six tries), and the best is kept: fewest role misses,
then fewest marks missing, then identity. A kit-1 shape (the Technical, the Tanker Rig) has no grammar to lean: it is built by its generator with the
role's part weights, the marks and the role pass, measured, and if it misses it is tried again from seeds derived from the asked one (`VehicleSpawnKit.Reroll`); the
seed that made it is in its name. A vehicle that still misses its role is kept and says so ("role missed (...)"): the spawn wants a vehicle, and the miss is reported.

**What it carries.** The name gets the role: "Bandit Box Truck 7 [raider, tier 2]" (never a committed seed's). The kit line: `// kit 4; role raider, tier 2, shape Box Truck,
role met, role plans 2, fit seats 3, guns 2 (medium), est. top speed 23 m/s, 2.4 t, armour 10.3, loot 1 (tier 2), 8.4 m, fuel 60 l, tow hitch, room, deck; seeds chassis 7
body 7 kit 7 wear 7, chassis ..., body room+gun bed, ...`; `Vehicle.Role`, `Vehicle.Fit` and `Vehicle.KitNotes` hold the same; the description starts "A raider (tier 2).";
the spawn's "generated" line carries the notes (`VehicleGenJob.RoleNotes`).

**The API.**

```csharp
game.VehicleSpawn.Request("Bandit", "raider", tier: 3, seed: null, ground, yaw, owner: 255, frontOf: 255, done: ...);
game.VehicleSpawn.Request("Highland", role: null, tier: 1, seed: 42, ground, yaw); // null: a role the style fields, by its weights
```

The server picks the shape from the role and the seed (`VehicleRoles.ShapeFor`: the same seed always picks the same), then makes it as any dynamic spawn (sliced, rerolled,
falling back to a committed seed of that shape), role and tier kept through the rerolls. `layers` works as for a shape request on the grammar's shapes. The request by shape is
unchanged. Nothing is on the wire: only the server generates.

**Which roles a faction fields** (`VehicleRoles.Fielded`, by style name, additive: `Styles.cs` is untouched; a style not named fields `DefaultFielded`):

| Style | scout | raider | gun platform | carrier | tanker | recovery | command | boss |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Bandit | 3 | 4 | 2 | 1 | 2 | 1 | 0.5 | 0.3 |
| IT | 2 | 1 | 1 | 3 | 1 | 2 | 2 | 0.3 |
| Call Centre (Scam) | 3 | 2 | 1 | 2 | 1 | 1 | 1.5 | 0.3 |
| Highland | 2 | 4 | 2 | 2 | 1 | 1 | 1 | 0.4 |
| British | 1 | 1 | 1 | 4 | 1 | 2 | 2 | 0.3 |
| Militia | 2 | 2 | 3 | 3 | 1 | 1 | 2 | 0.4 |

**Commands.** `--role "Bandit raider" --tier 2 [--seeds N] [--check] [--sliced]`: each seed's vehicle, how it met the role, its identity and what the role pass placed, the hit
rate and commonest misses; the first seed is made again and must write the same text, with `--sliced` once more as the dynamic spawn makes it (2.5 ms slices on the kit's
thread), whose hash must match; `--check` runs the full check on each. `--kit-report --roles [--seeds N] [--style S] [--role R] [--tier N] [--raw]`: every style, role and
tier (default 3 seeds a cell): a line a cell, then a table (met, failed, identity, every mark, plans, cost, shapes) and the same by role, by tier and by style.

**Measured** (2026-11-07, `--kit-report --roles --seeds 3`: every style (Bandit, IT, Highland, Call Centre, British, Militia) x every role x tiers 1 to 4, seeds 1 to 3,
576 vehicles, generation only; the 2-core box shared with other agents, load 6 to 12, so times are rough. Kit 3 is the same run with `--kit 3`: the same shapes and
seeds made without the role, measured against it):

| | kit 3 (no role) met | kit 4 met | kit 4 identity | kit 4 mean ms (kit 3) | kit 4's commonest misses |
| --- | --- | --- | --- | --- | --- |
| scout | 44/72 | 62/72 | 0.95 | 308 (276) | 9 est. top speed under 27 |
| raider | 40/72 | 59/72 | 0.96 | 369 (316) | 7 no loot (a Technical), 5 over 9.5 m |
| gun platform | 40/72 | 67/72 | 0.97 | 569 (485) | 5 under 2 guns |
| carrier | 4/72 | 62/72 | 0.97 | 1383 (760) | 10 armour under the need |
| tanker | 0/72 | 72/72 | 0.94 | 183 (203) | |
| recovery | 0/72 | 68/72 | 0.96 | 429 (421) | 4 no winch |
| command | 4/72 | 62/72 | 0.97 | 875 (638) | 10 armour under the need |
| boss | 1/72 | 45/72 | 0.98 | 1628 (751) | 18 armour, 9 under 3 guns |
| tier 1 / 2 / 3 / 4 | 35 / 34 / 32 / 32 of 144 | 130 / 127 / 125 / 115 of 144 | 0.96 / 0.96 / 0.96 / 0.97 | 645 / 695 / 744 / 787 | tier 4: 19 armour |
| all | 133/576, 24 failed to generate | 497/576 (86 %), 2 failed | 0.96, every mark on 574/574 | 718 (481), worst 3314 | |

Kit 3's misses are what the roles are for: no winch, no chest on a tanker, no signals, too little armour, a single gun on a gun deck. Kit 3's 24 failures are kit-1
Technical seeds with nowhere to stand a part; kit 4 tries a derived seed. Kit 4 re-planned 305 plans for a role over the 576 (about half a plan more a vehicle), which is its
cost; the slowest are the armoured War Buses, whose appliqué tests every part's bounds per plate. By style kit 4 met 79 % (Call Centre) to 91 % (Highland); identity held at
every tier (0.92 Militia, its derived signature, to 0.99 British), and every vehicle that generated carried every mark.

What is left over: armour is the hard need. A room's area is its hull bays (armour 8), and appliqué covers at most half their sides (plates a metre apart, so no two share
a face), so a War Bus tops out near 13 to 14 and the tier 4 needs (carrier and command 12, boss 12.5) miss about one in eight. A heavy vehicle's engine is the style's only
heavy engine, so a tier shows on it in armour, guns, size and loot, not part quality. The speed is an estimate, not a drive.

**Checked** (the full check, `--role ... --check`: drive, turn, guns, coplanar faces, rooms): Bandit, IT, Highland and British, every role, tier 3, seed 5: 32 of 32 passed.
Earlier samples found two things now fixed: Armour Plate in a style's decks (the style's own plate materials only now) and a tier 1 Militia Technical under the check's
stability (a kit-1 shape's role vehicle is held to its check's stability now), and one not the roles' doing: the Network Switch hood trophy z-fight (IT Box Truck 2), fixed on
main by the dressing fix. A role seed made twice writes the same text, and made as the dynamic spawn makes it (`--sliced`) the same hash; the dynamic kit-3 seeds tried
(Bandit Box Truck 1000, IT War Bus 77, Highland Flatbed Fort 55, Call Centre Gun Wagon 12, British Scrap Tanker 40, Militia Box Truck 5, Bandit Technical 30) write the
same files as before kit 4, byte for byte.

## Families and convoys

Kit 4 makes one vehicle for a role. **Families** make model lines of them, and **convoys** make groups that read as a fleet (`VehicleFamilies.cs`, `VehicleFamilyNames.cs`,
`VehicleConvoys.cs`; roadmap phase 6). Nothing here changes a vehicle made any other way: every committed seed, every request by shape and every kit-4 request by role
makes the same text as before (`--gen-vehicles`: all 123 identical; `--kit-report --roles --raw` for every IT role, tier and two seeds: the same numbers as main).

**A model** (`VehicleModel`) is a small record: style, role, the family's base tier, seed, mark (1 to 3), variant, paint scheme, layer overrides and a named flag. The same
model always makes the same vehicle, so its key is all a world, a log or a convoy needs to spawn it again: `Bandit/raider/2/282030/mk2/scheme 292943/wear 419532`
(`VehicleModel.Key`, `TryParse`; `VehicleSpawner.RequestModel` spawns one). Every family vehicle's kit line starts `model KEY`, and the spawn's "generated" line carries it.

**Marks.** A family is a style, a role, a base tier and a seed. The seed picks the shape (`VehicleRoles.ShapeFor`, so every mark is the same shape) and the layers. Mk1 is
made at the base tier, Mk2 a tier up and Mk3 two (to 4), but their frame leans by the base tier (`RoleAsk.FrameAt`: segment lengths, the chassis table's axles, cabs,
bed sides, plate materials and running gear), so the marks share one body on one chassis. The kit follows the mark's tier: guns weigh by it, the loot tier rises, and the
appliqué grows (`RoleAsk.KitArmourLean`: at least 0.3 for a Mk2, 0.5 for a Mk3). A room's hull bays keep Mk1's appliqué (kit 4's, at the frame's lean: plates high on a hull
raise the centre of mass, and a mark that failed the stability gate for them went to another plan, another body), and the mark's own goes on the sides, low (`Vehicle.SideArmour`:
the style's heaviest armour plate a metre square, welded upright 4 cm proud of the outermost plate or beam at a station, 0.35 m and more over the ground and its middle under
the centre of mass, so it only steadies the vehicle, only where it meets nothing, a metre from any other weld; two on a Mk2, four on a Mk3, more as the lean asks). A mark is made from its **first plan that builds**, never planned again for its role or its identity:
re-planning would give a later mark another body. So a family's mark misses its role more often than a kit-4 vehicle does, and says so in its kit line as any role vehicle
does. A plan under the check's stability throws as it builds (a family's mark has no role re-plan to catch a tall one), and the next plan is taken; a mark whose first
plans didn't build has "plan N" in its kit line, and `--family` flags a mark on another plan than Mk1's.

**Variants** change one layer on purpose, on the family's body and chassis: **up-armoured** (the kit layer on a seed of its own, and the most appliqué: lean +0.3, four side
plates and more), **gun truck** (the kit layer on a seed of its own, one more gun where the shape has a spot, every open bed a gun bed, one more allowed by its needs) and **tanker conversion**
(up to three Fuel Drums on its open deck, clear of the guns, under open sky, never coplanar with what is there; a loot tier up for the cargo). A kit-1 shape (the Technical,
the Tanker Rig) has no layers: its variants change only what the pass adds, and its marks are its seed's with the mark's tier.

**Named uniques.** A model is a named unique by a roll on its seed (`VehicleFamilies.RollNamed`: 8 % for a command or boss model, 2 % for the rest; `Named` in the key forces
it): its own name, a tier up, the best loot (loot tier 4), one more chest and one more mark (the style's masts, else its trophies, on the marks pass's spots).

**Names** (`VehicleFamilyNames.ByStyle`, by style name; Karl may edit them, and a changed list renames families without making any vehicle differently): each style has its
model names, how its marks read and its uniques' names, all ASCII. Bandit "Rustbucket Mk2", "Old Mother Rust"; IT "Model T-1000 Helpdesk", "Patch Tuesday SP2",
"Cloud Migration Enterprise", "The Mainframe"; the Call Centre "Hot Lead Gold", "Lucky Draw Platinum"; Highland "Glen Rover", "Bothy Bus II", "Bothy Bus Mor", "The Big Yin";
British "Wellington Mk II", "Windsor Mk III", "Her Majesty's Displeasure"; a style not named (Militia) gets `Default` ("Patriot Mk1"). Each variant has a style word: Bandit
"Plated", "Gunrig", "Guzzler"; IT "Hardened", "Pen Test", "Data Lake"; British "Ironclad", "Gunboat", "Bowser". A vehicle is named "Bandit Rustbucket Mk2 [raider, tier 3]",
never a committed seed's name.

**Measured** (`--family "STYLE ROLE" --seed 5 --tier 2 --check`, 2026-11-07, the 2-core box at load 3 to 9; distances as `--diversity` measures them):

| Family | Shape | Mk1 to Mk2 / Mk3 | up-armoured / gun truck / tanker / named | within the family (worst) | to two other families (least) | full check |
| --- | --- | --- | --- | --- | --- | --- |
| Highland raider 5 | Box Truck | 0.007 / 0.015 | 0.081 / 0.074 / 0.014 / 0.003 | 0.049 (0.112) | 0.375 (0.344) | 9 of 9 |
| IT carrier 5 | War Bus | 0.014 / 0.018 | 0.105 / 0.069 / 0.011 / 0.004 | 0.055 (0.115) | 0.220 (0.171) | 9 of 9 |
| British command 5 | Box Truck | 0.008 / 0.016 | 0.052 / 0.067 / 0.017 / 0.003 | 0.040 (0.083) | 0.229 (0.204) | 9 of 9 |
| Bandit raider 7 | Gun Wagon | 0.017 / 0.030 | 0.044 / 0.045 / 0.021 / 0.014 | 0.039 (0.075) | 0.305 (0.273) | 9 of 9 |

The marks read as one vehicle (under 0.08) and the variants as close kin; every other family of the role is three to seven times further away. What a mark changes
that the distance doesn't count: paint, loot, plate materials (none: the frame's), and the guns' classes.

**Convoys** (`VehicleConvoys.Plan(style, count, tier, seed, mix)`; nothing is made to plan one, and the same seed always plans the same):

- a **leader**: a command vehicle, or a boss (half as likely as a command vehicle from tier 3, an eighth below), a tier up;
- **escorts**: one raider or scout line (by the style's `Fielded` weights), Mk1s with a Mk2 among them now and then and a gun truck or an up-armoured variant on one; past
  three escorts, sometimes a second line for the last;
- **cargo** (from three vehicles): a tanker line, else a recovery or carrier line (weighted 3, 1, 1 by what the style fields); a second cargo vehicle is its twin, or for a
  line that isn't a tanker its tanker conversion;
- the order: half the escorts, the cargo, the rest of the escorts at the rear. A column, or from four vehicles a wedge (a third of the time): the leader in the middle, the
  rest alternately in a lane each side, 7 m out;
- **one paint scheme** (`VehicleConvoys.SchemeOf`): for every palette role but the rust, one of the style's colours (two of a role that lists four or more), so the
  convoy shares its body, hood, frame and war paint; every colour is still the style's own, so the identity's palette score holds. It works as `FactionStyle.Weigh` does: a
  per-thread `FactionStyle.Scheme` while a member is made, the same one roll over the scheme's colours, null for every other vehicle. Each member also takes its own wear seed
  (`Layers.Wear`, on the grammar's shapes), so twins differ in their rust, dirt and patches;
- **one or two model families** behind the leader (the leader is its own), and no two lines share a model name (a family seed whose name is taken is moved on).

**In the game.** `VehicleSpawner.RequestConvoy(style, count, tier, seed, at, heading, owner, done, mix)` plans it and queues its members; each is generated and set down as any
dynamic spawn (sliced, rerolled, falling back to a committed seed of its shape). Where a member stands is worked out when it is ready: the leader at `at`, each one after it
`VehicleConvoys.Gap` (5 m) behind the last tail in its lane (`Vehicle.ExtentZ`, the generated vehicle's own reach ahead of and behind its origin; a committed stand-in's from its
shape's usual), on the ground, heading the convoy's way. **Caps:** a convoy's members have their own cap (`ConvoyQueueMax`, 6, beside the 8 single requests, which they don't
count against), one convoy at a time on the server (`ConvoysAtOnce`), so a player has at most one; a request beyond it is refused with "a convoy is on its way already". One log
line per convoy, when its last member is down: `[vehicles] Bandit convoy 3: 4 vehicles, column, tier 2, 3 families, scheme 292943: 4 of 4 down in 7.2 s: leader Bandit Roadrash
Mk1 [command, tier 3] as 21 (KEY); escort ...; scheme body #6f7477+#5f6a55, ...`. The members' own "generated" and "set down" lines come as before.

**The parts menu.** With "Faction vehicles: new seed" on, the Blueprints tab has "A faction tile makes": *its shape* (as before), *by role* (a role, any by default, and a tier:
the tile's style, by role, `CActKind.FactionVehicle`, protocol 191) or *a convoy* (how many, 2 to 6, and a tier). A convoy is set down passing in front of the player: its leader
22 m ahead and to one side, heading across their view, the rest in a line behind it, so the whole convoy is seen side on. The host's "Roles and convoys" (on by default; off: requests
by role, model and convoy are refused and the tiles make their shape, as before kit 4) sits beside "Server generates them" (off: every member is a committed seed of its shape).

**Checked.** `--convoy STYLE --seed 11 --count 6 --tier 3` for IT, British, Highland and the Call Centre and `--convoy Bandit --seed 3` generate every member, the leader twice
to the same text. The full check passed every member of the table's four families (36 of 36) and 4 of the 5 of Bandit convoy 3; its leader (a tier 3 command
Box Truck, plan 1) stood 10.5 degrees off level and didn't drive: its chassis mixed Heavy Pogo Struts in front (they hang the wheel 1.375 m under the mount) with Heavy
Suspension behind, so it stood nose up on its tail rails. Fixed in the chassis (2026-11-07, "Things that caught the vehicles so far"): the leader now has its own rear on a
level front and passes the full check (71 m in 6 s, 1.7 degrees worst tilt); the other 39 members of eight convoys tried came out byte for byte the same. In game (an offline host on
Construct Yard and a client over RakNet at 80 ms and 2 % loss, the client asking by the wire): Bandit convoy 3 (4 vehicles, column) down in 7.2 s and IT convoy 11 (6, wedge)
in 20.2 s, spaced and laned as planned, the client with the same parts on every body, and every member's text hash the one `--convoy` prints for it (made in slices as the spawn makes it); the worst ticks were the first slice of a generation (21 to 33 ms, the same for a single role
spawn on this box) and one 52 ms join with a garbage collection in it.

## Cargo and spares

Roadmap phase 9 (`VehicleCargo.cs`): a kit-4 vehicle (asked for by role, or a family's model, or a convoy's member) carries what its crew would carry for itself: **ammo
for its own guns, spare parts for its own repairs and spare guns**, stowed the way a player would stow them. All of it is real parts, so players shoot it, spill it, steal
it and salvage it like anything else; nothing is abstract and nothing unlocks. Every other vehicle (a request by shape, a committed seed, kit 1 to 3) carries nothing new:
`--gen-vehicles` reports all 123 identical, and kit 3 seeds write the same text as before.

**What it carries** (`Vehicle.StowCargo`, at the end of the role pass, on forks of the kit layer: "kit cargo" for the amounts, "kit cargo shelf" for a spares shelf; the layout rolls nothing):

- **Ammo it uses.** For each gun, magazines of the kind its build takes (the build's `feed`: a Box Magazine for a carbine, an Extended Mag for a Scrap MG, a Shell Feed for
  a pump gun, a Harpoon Breech for a Whaler, a Fuel Canister for a flame gun); Ammo Boxes for the guns that load from boxes; and on a gun deck a loose Ammo Crate. Rounds
  are universal (docs/sandbox/CONSTRUCTS.md, "Ammo feeds": any type fits any gun, the bore sets the calibre), so a box serves every gun but a flame gun, whose ammo is fuel:
  it gets Small Fuel Tanks instead. A box or crate comes full (300 and 1,200 rounds), of FMJ: a slot or a cargo piece holds no setting, so the type is the default; a
  magazine out of a store comes empty, as any does (fill it at a box).
- **Spares from its own build**, chosen from its parts, the commonest first: wheels of its own kinds (front and rear may differ), its commonest spring, plates of its own
  materials and sizes (0.5 m and 1 m before quarter plates: a patch must cover a hole), its commonest frame beam (1 m and under), fuel for its engine (Small Fuel Tanks, and a
  Fuel Drum when it already runs on drums), a Battery Pack for an electric drive (a no-air context, "Spawn context").
- **Spare guns** by their parts: a receiver, barrel, muzzle device, action and magazine of a gun it mounts (the commonest first), and a spare barrel.

How much, at tier 2 (`VehicleCargoTables.ByRole`, keyed by role; a fraction is the chance of one more; ammo per gun):

| Role | magazines | boxes | crates | wheels | springs | plates | beams | fuel | spare guns | barrels |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| scout | 1 | 0.5 | 0 | 0.5 | 0 | 0 | 0.3 | 0.5 | 0 | 0.3 |
| raider | 1.5 | 1 | 0.3 | 1 | 0.3 | 1 | 0.5 | 1 | 0.3 | 0.5 |
| gun platform | 2 | 2 | 1 | 1 | 0.5 | 2 | 1 | 1 | 1 | 1 |
| carrier | 1.5 | 2 | 0.3 | 1 | 0.5 | 3 | 1 | 1 | 0.5 | 0.5 |
| tanker | 1 | 1 | 0.5 | 2 | 1 | 2 | 2 | 2 | 0.3 | 0.5 |
| recovery | 1 | 1 | 0 | 2 | 2 | 4 | 4 | 1 | 0.3 | 1 |
| command | 2 | 2 | 0.5 | 1 | 0.5 | 2 | 1 | 1 | 1 | 1 |
| boss | 3 | 3 | 1 | 2 | 1 | 3 | 2 | 2 | 2 | 1 |

A tier scales every amount (0.7 at tier 1, 1 at 2, then 0.3 a tier more), and a style its ammo and its spares (`VehicleCargoTables.ByStyle`, additive: `Styles.cs` is
untouched): the Bandits carry 1.3 times the spares (they scavenge everything), the IT Department 0.8 of both (it travels light), the Call Centre 0.8 and 0.9, Highland 1.1
times the ammo, the British 1.1 times the spares; a style not named (Militia) 1 and 1. A vehicle carries at most 18 loose pieces, of at most 12 % of its own mass.
`Constructs/Loot.json` is untouched: the spares come from the vehicle's own parts, not a table, and a role's loot still rolls as before.

**Stowed as a crew would** (the order when room runs short: Ammo Boxes, magazines, crates, fuel, wheels, a gun's parts, springs, plates, beams):

- **In slots** first, for anything that is an item: the free slots of its storage (room furniture, chests, shelves, a deck chest), nearest the gun for ammo, by the game's
  own slot rule (`ConstructStorage.Fits`: the size class and shape; a shelf's rack slot piles as many of one kind as fit). A container that rolls loot keeps at least half
  its slots for its loot. A room with no slot left gets one **spares shelf** (the style's `shelf`, else its `chest`) against its end wall, out of the room's middle, which
  rolls no loot. The blueprint lists them as `"stored"` (docs/sandbox/CONSTRUCTS.md, "Blueprints"): set in at spawn (`ConstructLoot.Stow`), the same every spawn, before
  the loot rolls into what is left.
- **Loose**, for physics-only pieces (plates, beams, Dually Wheels, a Battery Pack, Ammo Crates) and what no slot took: on its open decks (the bed, a fort's deck, the porch,
  then a roof rack for pieces of 16 kg and under), each deck with a frozen **Cargo Volume** (its control box on a free socket on a deck plate's side, its box round what was
  laid with 15 cm to spare, on the configure tool's 10 cm steps). Pieces lie on the deck's 12.5 cm grid nearest where they are used (ammo by its gun, spares at the back),
  3 cm apart and from anything built, plates, beams and wheels flat in their thinnest turn (a wheel that won't lie flat stands on its tread), plates stacked four high,
  beams three and wheels two, each a few millimetres off the one below so no faces lie flush. Nothing stands where a turret's arm or seat swings (under the ring's top within
  its reach), on a hole or over a deck's edge. A Technical's own crates (the shape's `CargoBay`) keep their place; the pass lays round them.

**In play.** A stored item is a slot's part: taken by hand (F), shot with its container, spilled when the container goes; it doesn't cook off. A loose piece is a body of its
own riding in the frozen volume: shot, lifted out and carried off; shoot the volume's control box and everything it held goes free at the vehicle's speed. **A loose Ammo
Box or Crate cooks off when shot**, as any box does (`ConstructWorld.DamagePart`), so a gun platform's crate on its open deck is a weak point the brief names
(`BriefStow.CooksOff`); a Fuel Drum or tank on the deck burns as it would anywhere. The loose pieces' mass is real: the drive check builds the vehicle with them aboard,
and the role's speed estimate counts them (`Vehicle.SpareMass`); the role's loot need counts neither (`SparePieces`), so a scout isn't over its loot for a spare magazine.

**What it says.** The kit line carries a note (`cargo 4 ammo (1 loose), 3 magazine loose, 2 wheel loose, 4 plate loose, 1 fuel loose; 170 kg loose`), `Vehicle.Stowage`
holds the manifest (kind, part, count, where, what for) and `Vehicle.KitCargoMisses` what was left behind and why. The brief's `Stowed` ("Behaviour data") lists each stack
and loose piece with the guns it serves (a magazine the guns that take it, by the workbench's rule; rounds every gun but a flame gun; fuel the flame gun), so an AI can
rearm and patch from its own cargo; the brief's summary ends `cargo 17 (6 ammo and magazines, 1 loose rounds)`.

**Measured** (2026-11-07, `--cargo all --seeds 1` and the same with `--no-cargo`: six styles x eight roles x tiers 1 to 4, seed 1, 192 vehicles; the 2-core box shared
with other agents):

| | without (`--no-cargo`) | with |
| --- | --- | --- |
| roles met | 166/192 | 166/192, the same vehicles |
| identity | | every vehicle's score the same |
| drive check | 283 plans, 4 refused | 283 plans, 4 refused (the same); the throttle pulse 0.07 m shorter on average (-0.7 to +1.1), at least 2.0 m on every vehicle (the gate is 1 m) |
| generation, warm | 650 ms a vehicle | 713 ms (the cargo pass 17 ms a plan, worst 65) |
| carries something | | 181 of 192 (Militia and Call Centre scouts and Militia raiders, Technicals with no deck and nothing rolled) |
| full check (`--role ... --tier 3 --seed-list 5 --check`, Bandit, IT, Highland, British x every role) | | 32 of 32 passed |

Pieces and loose mass a vehicle by role: scout 1.9 (19 kg), raider 6.0 (61 kg), gun platform 15.7 (147 kg), carrier 13.7 (70 kg), tanker 6.9 (92 kg), recovery 11.3
(111 kg), command 16.1 (68 kg), boss 21.5 (91 kg); 82 kg a vehicle, 3.5 % of its mass. Over the 192, in slots / loose: ammo 402 / 269, magazines 64 / 542, gun parts
14 / 515, fuel 10 / 141, springs 1 / 57, and wheels 61, plates 86, beams 69 all loose. Left behind for want of deck room (a room-only War Bus or Box Truck has no open
deck, a Gun Wagon's hutch is small, and nothing stands in a gun's line of fire): beams 81 times, plates 68 and armour plates 44, wheels 124, fuel tanks and drums 91,
crates 43. The first seed of a role made as the dynamic spawn makes it (`--role "Highland tanker" --tier 2 --sliced`) gave the same hash as made directly; a convoy
(`--convoy Bandit --seed 3`) made every member and its leader twice to the same text; a brief with its cargo saved and read back the same (`--brief`, 1,460 bytes).

A probe (a throwaway command, not kept) built role vehicles from their text in a world of their own, as a spawn does: the stowed items were in their slots beside the
rolled loot; a destroyed Chest and a destroyed Crate Stack spilled their pieces (3 and 5); a destroyed Cargo Volume let its 11 and 18 carried pieces go; a loose Ammo Box held 300 rounds, and bolted to the vehicle's
gun (the attach rules, `ConstructBuild.Place` and `Attach`) it added its 300 rounds to what the gun could draw.

**Not done.** No new part: there is no gun rack or tool rack part, so spare guns lie as parts in slots or on a deck, and a style's racks are its shelves. A room-only vehicle
leaves its physics-only spares behind (a cargo volume on a room floor would stand in the aisle the room check walks). A box's ammo type is the default (slots and cargo hold
no settings). Spares aren't added to the identity's weak points (they ride on another body): the brief's `Stowed` names them instead. Nothing was tried through a
player's own input in the game window (taking a box out with F, the attach tool).
