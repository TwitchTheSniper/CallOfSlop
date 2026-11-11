# Vehicle kit: shape grammar, variety and identity

Kit 2's shape grammar, kit 3's layered variety, identity and weak points. Part of the vehicle kit guide: index in VEHICLEKIT.md.

## Shape grammar

Kit 1 made each shape with one generator: a Box Truck was always a hull on a chassis with the same nose, room and tail in the same places, so seeds differed in parts
and hardly at all in outline (Bandit Box Truck, November 2026: silhouette 0.057 of the distance against 0.39 for parts). **Kit 2** (`VehicleGrammar.cs`) assembles the
heavy shapes from **modules** instead, so the outline varies with the seed while every part still comes from the style.

**Which kit.** `VehicleKitVersion.For(generator, seed)`: `Force` (`--kit N`), else kit 1 for a shape with no recipe (the Fuel Trailer
and any new generator; the Double Decker is the grammar's since body styles phase 5, the Technical and the Tanker Rig since phase 9b, BODY-STYLES.md section 20) or a seed in `VehicleKitVersion.Frozen`, else the current kit: **kit 5 since 2026-11-07** (the user's decision), kit 3's
grammar and layers (or kit 4's role) with its rooms lofted in the style's body language ("Body styles" in BODY-STYLES.md). Every committed seed of a shape with a
recipe was made again by kit 5 that day (continuity wasn't required: a saved world holds built constructs, not seeds), with new seeds picked from each shape's
sweep, so `Frozen` is empty and `--gen-vehicles` reports every file identical. Kits 1 to 3 still make what they made (`--kit 2` byte for byte). A kit file says
so in a comment after its first brace (`// kit 5; body room+bed, cab caged, ..., language slab 2.21 m, ...`; blueprints allow comments), `Vehicle.Kit` and
`Vehicle.Modules` hold the same, and the spawn logs it. A seed committed from now on is made by the kit current when it was committed: grade and commit from a
sweep of that kit; when a later kit changes a committed seed, either the shapes are committed again or the seeds go into `Frozen` with their kit. A role's,
family's, convoy's or context's vehicle is kit 5 too (kit 4's role on lofted rooms); `--kit 4` makes it with fixed bays.
Phase 2 (BODY-STYLES.md section 7) made the committed seeds again the same day; a room's door may now be on either side or in its back (`Room.DoorFace`
says which: anything that walks to the door or keeps out of its way reads it, never assumes +x), its bays run 0.75 to 1.5 m (read a bay's length from its
key, `HullLoft.ShapeOf`, not its bounds: caps stand past it), and its turret and gear spots are judged by distance along the room with sizes from the parts
(`BaySpots.Turret`).

**Modules** (`ShapeGrammar.Catalogue`). Each has a family, a name, the style parts it is made of (a module whose parts the style doesn't list is never made), and its
connection rules as tags: what it needs, what it provides, what it can't stand beside, and the gear recipes it allows.

| Family | Modules | Rules |
| --- | --- | --- |
| cab | caged, enclosed, armoured, cab-over; kit 5 also crew, merged | from the style's `heavyCab` list; kit 5 (lofted) from `BodyStyleRule.Cabs` instead, whatever the list; crew needs a metre of bare rails first, merged a room first; cab-over and merged provide "cabover" |
| nose | long hood, short hood, plated hood, ram, flat | from the style's `hood` list (ram: its `nose` list's Ram Plow or Bull Bar on any hood); flat needs a cab-over, the rest exclude it |
| body | room, bed, gun bed, hutch, gun bay, tank, gap | segments front to back (below); a tank allows only the Scrap Tanker's gear |
| rear | porch, porch hitch, gun deck, hitch, bare | porch carries the drum; a gun deck needs a room last; hitch and bare need fuel or a drum carried ahead |
| under | stilts, tub, skirted | tub and skirted need a high floor (a low one is always on stilts); skirted needs a room; tub can't go round a tag or tandem axle |
| flank | bare, panniers, loaded | a room's waists |
| sides | walls, rails, open | a bed's edges; walls allow only the Flatbed Fort's gear (they hang under the floor's edge) |
| roof | none, rack | a rack needs a room with a metre bay: deck plates and tube rails over two flat tall bays side by side |

**Body segments** run back from behind the cab (z 0.2): a **room** is a hull of 3.5 m and up (brow, metre bays, the 1.5 m door bay, tail) whose bays the seed walks
between tall, low and narrow, with its roofline changing at least once (twice from three metre bays), the door bay tall; a **bed** (gun bed: with a deck gun) an open
deck in half metres; a **hutch** the Gun Wagon's deck; a **gun bay** the Scrap Tanker's gun on its own sill; a **tank** its barrel on cradles; a **gap** bare rails between
the cab and the body. The rear axle stands 1.25 to 2.25 m in from the body's end, by the seed.

**Recipes** (`ShapeGrammar.Recipes`): a shape name is a set of body patterns, rears, a gun count and gear. The Box Truck is a room, sometimes behind a gap, with an open
bed or a gun bed behind it or a gun bed ahead of it; the War Bus a long room (5.5 to 7.5 m); the Flatbed Fort a gun bed and a bunker either way round; the Gun Wagon a
hutch, a gun bay and a gun bed, or a crew room and a gun bed; the Scrap Tanker a gun bay or a crew room before its tank. So the spawn API's and the committed files'
shape names all still work.

**What the assembler keeps to.** One fuel tank feeds the engine (an engine has one inlet): a tank segment, else a drum on a bed, the hutch or the gun deck, else a porch.
Guns go where their mounts clear: deck guns half a metre from the stilts and clear of every axle, a gun behind a room faces aft (never a Whaler, whose magazine is
where the seat goes); roof turrets only on flat tall bays (on a sloped one the arm runs into the next bay), a flame gun never on a sponson (it doesn't follow the look
round), and a long gun on a low mount near the front not at all (it would want a pedestal). Only a room's guns are fed from its magazine, worked from its seats: a belt
from the room to a bed gun would cross the room where a player walks; the others carry their own box, a hutch's gun its feed. A short room has one seat and its loot
against the end wall first (`Vehicle.EndChest`), never in the middle, where the check stands a player, and Trim's crooked patches stay off the hull bays (they would land
inside). Finally the built body is measured as the check measures it: stability under 0.63 or a side or plan fill within half a point of `MaxSideFill`/`MaxTopFill`
throws, and so does anything the game refuses; then **the seed plans again** from the next fork (`"grammar 1"`...), gear included, up to four times. A retry shows in the
kit line as "plan 2". A plan that builds is then scored for its faction ("Identity and weak points"): under the style's threshold, or missing one of its marks, it is
planned again once, and the better of the two is kept.

**Seeds.** All of it rolls from forks of `v.Dice` ("grammar", "grammar build", by attempt); the module bodies use the same forks kit 1's code does ("deck bed", "hood",
"weapon"...), so a seed is the same text on every machine. Generation yields where `BlueprintScript` does. Kit 3 splits these forks into layers ("Layered variety").

**Measured** (2-core box, generation only unless said; kit 1 is `--kit 1` or the committed seeds' state before the change):

| | kit 1 | kit 2 |
| --- | --- | --- |
| Bandit Box Truck, seeds 1-20: silhouette part / nearest neighbour / mean pair | 0.057 / 0.066 / 0.162 | 0.116 / 0.128 / 0.222 |
| Bandit Box Truck: seeds closer than 0.08 | 14 in 6 groups | 2 in 1 group |
| Highland Box Truck, seeds 1-20: silhouette / nearest neighbour | 0.051 / 0.076 | 0.103 / 0.120 |
| Bandit, every shape, 6 seeds each: nearest neighbour | 0.112 | 0.134 |
| Highland, every shape: nearest neighbour; Box Truck and War Bus apart | 0.103; 0.212 | 0.126; 0.230 |
| Generation failures, Bandit and Highland, 10 seeds of each recipe shape | 1 of 100 | 0 of 100 (with retries) |
| Full check (`--sweep-vehicle`), Bandit Box Truck / War Bus / Flatbed Fort | 8 of 12 / 4 of 10 / 2 of 10 | 12 of 12 / 12 of 12 / 10 of 10 |
| Generation, mean (worst) per seed, Bandit | 247 ms (652) | 242 ms (1196, a retry) |

Kit 1's check failures (a chest in a short room's middle, a crooked patch inside a hull, a boxy bus) are what the grammar's rules above avoid; kit 1's committed seeds
passed, and its code is unchanged. The Silhouette rules are the grammar's rules: separate masses (a cab, a gap, a room, a bed or a tank, each its own height), a roofline
that steps, wheels in the open, a room only for a crew and loot.

**Adding to it.** A module is a row in `Catalogue` plus its body in `ShapeGrammar.Build` (reuse the kit-1 generator's code for it, put where the plan says); a recipe is a
row in `Recipes`. Sweep the shapes it can appear in with `--sweep-vehicle "Bandit Box Truck" --seeds 12 --kit 2` (the full check) and measure with `--diversity ... --kit 2`.

## Layered variety

The user's reviews of kit 2: "all use about the same wheelbase and suspension", "still too boxy", "interiors are too alike". **Kit 3** (`VehicleKitVersion.Layered`, the current
kit) is kit 2's grammar with its variety in four independent layers, each rolled from a root of its own (`Vehicle.UseLayers`, `Dice.Routed`), so one can be kept while
another rerolls:

| Layer | What it decides | Where |
| --- | --- | --- |
| chassis | the corners (spring and wheel at the front, at the rear with its differential and the floor height it allows), wheel and spring swaps within a class, the axle layout (one rear axle, a tag, a pusher, a driven tandem, at 1.25 or 1.5 m), where the front axle stands, how far in from the body's end the rear one is, the front track, what the floor stands on (stilts, tub, skirts) | `VehicleChassis.cs`, `LayerTables.Chassis` |
| body | the body pattern and segment lengths, cab, nose, rear, bed sides, roof, the room's bays (heights, steps, rake), the decks' and walls' plates, the hood, the ladder, paint | `ShapeGrammar.Plan`, `LayerTables.Body` |
| kit | the guns (how many, where, which), the flanks' panniers, the room's furniture and chest, the ammo, ram bars, spikes, trophies, masts, the Pipe Box, a passenger | `ShapeGrammar.Guns`, `LayerTables` has no kit table: the style's own lists |
| wear | an age; panels gone over to the style's rust, low panels dark with road dirt (the frame colour), soot round guns and the exhaust (the palette's darkest), patch repairs welded over outer panels, an open deck's edge rusted out; the style's crooked plates (Trim's "crooked" fork) | `VehicleWear.cs`, `LayerTables.Wear` |

**How a fork finds its layer.** `VehicleLayers.LayerOf` maps a root fork's topic to a layer: "chassis", "gear", "axles", "drive" are the chassis's; "wear", "crooked" the wear's;
"grammar kit", "weapon", "rounds", "ram", "show", "kit", "piper", "furnish", "end chest", "cargo", "crew", "panniers" the kit's; anything else the body's. So `Vehicle`'s
own methods (`Deck` forks "deck bed", `Turret` "weapon 2", `Trim` "paint") roll from the right layer without knowing about layers. A new fork in a kit-3 path picks its
layer by its name: name it for what it decides. A layer left unset takes the seed, and a root of the seed's own number forks exactly as the plain dice would.

**The API.** `VehicleLayers(Chassis, Body, Kit, Wear)`, each an optional seed. `VehicleRegistry.Make(g, seed, layers: new(Wear: 99))` is seed 7's truck with seed 99's wear;
`new(Chassis: 42)` the same body on another chassis. `game.VehicleSpawn.Request(style, shape, seed, ground, yaw, ..., layers: new(Wear: 99))` does the same in game
(nothing on the wire: only the server generates). A vehicle with overrides is named for them, "Bandit Box Truck 7 [wear 99]", so it never reads as a committed seed, and
`Vehicle.Layers`, `VehicleGenJob.Layers` and the kit line carry every layer's seed: `// kit 3; seeds chassis 42 body 7 kit 7 wear 7, chassis 3 axles, tandem 1.5 m, front
under the hood, inset 1 m, coil/hydraulic, wing 0.5, wear age 0.64 (11 rusty, 15 dirty, 0 sooty, 1 patched, 1 rusted out), body room, ...`. Only the grammar's shapes
have layers: the Technical, the trailers and the Tanker Rig refuse them (kit 1; the Double Decker has a recipe since body styles phase 5), and so does a request for them.

**What "independent" means.** Each layer's dice are its own, but what it builds sits on the others: a new chassis moves the rear axle, so the guns' spots clear of it can move
(kit decides which gun, the axles where it may stand), and a different kit puts panniers on, so the wear's rust lands on a different set of panels. Measured on Bandit Box
Truck 5 with each layer varied over 8 seeds (`--vary`, mean pair distance): wear 0.000 (it moved no part and no trait, only paint, patches and deck edges, which the
distance doesn't count); kit 0.068 (only the guns, stores and dressing: length, wheelbase, track, suspension, body and cab never varied); chassis 0.099 (wheelbase and
track 4 and 3 values, 4 kinds of suspension; length, body, cab and guns never varied); body 0.191 (outline, length 6 values, cab 4; the axle layout 7 of 8 the same, and
the suspension follows the body where a body module limits the gear, as a walled bed or a tank does).

**Ranges.** `LayerTables` (`VehicleLayers.cs`) holds a table per layer by style name, so `Styles.cs` is untouched and a style not named (Militia) gets the defaults:

- `ChassisRange`: the rear axle's inset (quarter metres, kit 2: 1.25 to 2.25 in halves), the front axle's stations and their weights (-1.675 under the hood's back, kit 2's;
  -1.925 forward; -0.925 set back beside the cab), the front wing (0.5 or 0.75 m), the axle layouts' weights (second axles weigh 0.3 times under a 4 m body up to twice
  under a 9 m one), their spacings, and the chances of mixing recipes' corners, of a wheel or spring swap, and of a dead tag axle on Rigid Mounts. A cab-over keeps kit 2's
  front axle; a moved front axle keeps kit 2's track and leaves the hood's fenders off (`Vehicle.NoFenders`), which stand where kit 2's wheels are.
- `BodyRange`: how segment lengths lean in the recipe's range, the room sections' height weights (tall, low, narrow), how often the roofline steps, how often a brow and
  a tail are raked down. The defaults are kit 2's rolls.
- `WearProfile`: the shares of rusty, dirty and sooty panels, patch repairs, rusted-out deck edges, and the age range that scales them. Bandit heavy, the IT Department
  light, the Clan "well loved" (mud and mended, little rust), the Ministry and the Call Centre between.

The corners come from the style's own `HeavyGear`: a chassis takes one of its recipes, then (by the table's odds) the front or the rear of another recipe (the rear with its
differential and floor height: a swing arm's throw is what lets a floor sit low), a wheel of the same class (at the rear under a body, never a bigger one: a wheel's top meets
the walls hung under the floor) and a spring of the same mount (Leaf Spring, Heavy Suspension, Hydraulic Suspension; Double Wishbone, Heavy Double Wishbone; Swing Arm,
Heavy Swing Arm). A second axle needs a high floor on a wide differential and the shape's own gear list to allow it ("tandem" for tag and pusher axles, "six" for a driven
tandem), so a Flatbed Fort's walled bed or a tanker keeps to what kit 2 let them have. A pusher that would stand under the cab goes behind instead. "Walking beam" and "bogie"
suspensions have no part of a size for these vehicles (the Bogie parts are tracks a metre and a half across): a close tandem stands for a bogie. "None" is the dead axle.

**Wear and the checks.** Wear never touches a room's hull (rays out of a room find no new hole), a fuel tank, a tyre, a gun, the frame (stacked beams of one colour don't
fight) or anything with slots, and its paint is only ever one of the style's own colours, because the identity score counts a painted part as the style's only when its colour
is in the palette: a mixed "rusty beige" read as foreign paint and cost the Bandit Box Truck 0.04 of identity before it was changed. So for every style but the Bandits, whose
"rust" role is rust, rust shows as their panel colours and the wear reads mostly as dirt, soot, patches and missing plates. A patch is a freeform weld, which the build rules
don't check, so `Vehicle.ClearOf` tests its box against every part's bounds first. Deck cells only rust out on an open deck's edge (bed, porch, rack, plated hood) and never
beside a hole or a whole plate. **Not done:** bent trim. (Kit 5's lofted cabs carry their windscreens and back windows as glass parts of their own since body styles phase 3, so a shot breaks the glass, not the cab.)

**Two fixes the wider ranges needed.** A driven tandem under a long kit-3 wheelbase ran its laid shaft more than 8 m in one segment, which the game refuses: past 7.9 m the
shaft takes a node halfway (`Vehicle.Powertrain`; shorter shafts are laid as before, so kit 1 and 2 are unchanged). And a kit-3 plan must measure a point under the side
and plan fill limits, not half a point: one built at 62.0 % after measuring 61.6 %.

**Measured** (2-core box shared with other agents, load 3 to 8, so times are rough; `--kit 2` against `--kit 3` on the same build, which makes kit 2 byte for byte as before):

| | kit 2 | kit 3 |
| --- | --- | --- |
| Bandit Box Truck, seeds 1-20: mean pair / nearest neighbour / least | 0.222 / 0.128 / 0.074 | 0.238 / 0.179 / 0.157 |
| the same: layout part / silhouette part; seeds closer than 0.08 | 0.266 / 0.116; 2 | 0.306 / 0.118; none |
| the same, traits: wheelbase / wheelbase share of length / track / wheel sets / suspension kinds (values) | 8 / 7 / 3 / 4 / 5 | 10 / 7 / 6 / 8 / 10 |
| Highland Box Truck, seeds 1-12: mean pair / nearest / layout; suspension kinds, wheel sets, 3-axle share | 0.220 / 0.143 / 0.260; 4, 3, 33 % | 0.239 / 0.167 / 0.315; 8, 7, 58 % |
| IT War Bus, seeds 1-12: mean pair / nearest / layout; suspension kinds, wheel sets | 0.180 / 0.128 / 0.210; 5, 4 | 0.203 / 0.143 / 0.227; 6, 5 |
| Highland, every shape, 6 seeds each: mean pair / nearest neighbour | 0.328 / 0.126 | 0.333 / 0.140 |
| IT, every shape, 6 seeds each: mean pair / nearest neighbour | 0.326 / 0.119 | 0.332 / 0.134 |
| Identity mean by style, `--kit-report --seeds 4` (Bandit, IT, Highland, Call Centre, British, Militia) | 0.95, 0.90, 0.95, 0.88, 0.91, 0.91 | 0.96, 0.89, 0.95, 0.88, 0.91, 0.91 |
| Weak points all there and hittable, same run | 31/31, 26/32, 32/32, 21/32, 34/34, 32/32 | 30/30, 24/32, 32/32, 21/32, 34/34, 32/32 |
| Generation failures over every style and shape, 4 seeds | 3 of 196 (Technical, kit 1) | 4 of 196 (the same 3, a Bandit Scrap Tanker) |
| Full check (`--sweep-vehicle --seeds 6`): Bandit Box Truck, IT War Bus, Highland Flatbed Fort, British Gun Wagon | 6, 6, 6, 6 of 6 | 6, 5, 6, 6 of 6 |
| Generation, mean over every style and shape (load varied) | 312 ms (worst 2182) | 348 ms (worst 1840) |

Kit 3's one full-check failure (IT War Bus 5) is a Network Switch trophy on a Long Hood's top socket, coplanar with the V8 Engine under it: a pairing kit 2 can roll too, not
the layers' doing. Fixed where dressing stands (2026-11-06): `Dress` and the marks fit with `Onto(..., clear: true)`, which refuses a spot where a face would be
coplanar with a part already there (`VehicleChecks.ZFightsWith`, the check's own rule on the parts nearby), and the part goes to the next panel. The cause is in the
data: every kit engine's details stand above its +y socket (the V8's rocker covers 12 cm, the Engine's 11 cm) and poke through a Long or Snub Hood's skin (5.5 to 6.5 cm
at its middle), which a hood's shape would have to change to hide. The IT War Bus's lower identity is its old missing mast (5 of 12 against 3 of 12): a bus that plans again
more often (a boxy plan, an ammo reel) has fewer plans left for the identity re-plan, and that is what the guaranteed-marks pass is for. Generation cost is within the
box's noise: more plans are made (the fill margin is a point now) and each costs the same.

## Identity and weak points

As the grammar makes more kinds of outline, every vehicle must still read as its faction, and it must still have the cheap, exposed parts a player shoots to stop it
without wrecking what is worth salvaging (the loot loop: disable, then strip). `VehicleIdentity.cs` scores a built vehicle for both. It runs no physics and uses no dice: 15
to 50 ms a vehicle on the 2-core box.

**A style's signature** (`StyleSignature`). `FactionStyle.Signature` when a style sets one; else the table `VehicleIdentity.Signatures`, by style name (the shipped
styles, written from what this file and their slots say; `Styles.cs` is untouched); else one derived from the style's own dressing slots (`Derived`: its spike, trophy, mast,
gear and piper parts are its characteristic parts and its one mark; tyres and fuel its weak points), which is what Militia gets. A signature holds:

| Field | What it is |
| --- | --- |
| `Marks` | Features every vehicle carries (`StyleMark`): at least `Min` of some parts, on every vehicle that meets `When` ("seat", "room", "heavy", "light"; empty: all), and with `Open` > 0 seen from that many of the six directions. |
| `Parts`, `PartTarget` | Characteristic parts and how much each says "this style" (a name counts at most three times); `PartTarget` of it reads as fully this style. |
| `Modules` | Characteristic grammar modules by family (`cab`, `nose`, `flank`...), each family judged against its best; a module not named counts 0.3. Kit 2 only. |
| `Silhouette` | The outline range (`SilhouetteRange`): side fill, plan fill, height over length, roofline wander and rake (how far the ends drop below the highest point). |
| `WeakPoints` | The designed weak points (`WeakPointRule`), below. |
| `Threshold` | Under this (default 0.75), or with a mark missing, a kit-2 plan is planned again. |

A part is picked by name or by a role keyword (`PartPick`): "fuel" (any fuel tank: a drum, a tanker's barrel), "tyres", "driver" (the driver's seat), "seat", "engine",
"ammo" (ammo boxes and crates).

**The score**, 0 to 1, six parts weighted into one (brackets: before the section, 2026-11-07, as `--no-sections` measures); a part with nothing to judge (no paint, no marks that apply, no seat for traits: a trailer's dressing is its tower's) is
left out and the rest weighed up:

| Part | Weight | What it measures |
| --- | --- | --- |
| marks | 0.30 | The share of the marks that apply which are there (and in the open where asked). |
| palette | 0.15 (0.20) | Painted parts in the style's colours (0.6) and how many of its palette roles show at all (0.4). |
| traits | 0.20 | Characteristic parts against `PartTarget`, and for a grammar vehicle the mean of its modules' weights; the mean of the two. |
| silhouette | 0.10 (0.15) | Each of the five outline numbers in range scores 1, falling to 0 a quarter of the range (plus 0.05) outside it. |
| weak points | 0.15 | Each rule that applies: 1 there and hittable, 0.25 there but covered, 0 missing. |
| section | 0.10 (none) | Each room bay from the front (`FrontProfile`) against the nearest of the style's body languages as the style draws them (`BodyStyleRule`): within `SectionFloor` (0.008 mean difference) reads 1, `SectionScale` (0.03) further 0; the mean over the bays. No room: left out. A pod on a Bandit truck reads less Bandit; kit 3's fixed bays read about 0.45 on an IT bus. |

The breakdown reads `0.78 as IT: marks 1/2 (no mast or dish); palette 100 % (4/4 roles); traits 0.92 (parts 1.00, modules 0.83); silhouette 5/5; weak points 2/3
(antenna missing, tyres 6/6 hittable, fuel 1/1 hittable)`.

**Weak points.** A rule names a part pick and how exposed it must be: seen from `MinSides` of the six directions (a ray from far off along each axis, at the part's middle or
one of four points round it, meets that part first), and hit first by a ray from a standing player's eye (1.6 m over the ground under the wheels) `Range` metres away (20 by
default, and outside the vehicle's footprint) from `MinBearings` of 16 bearings round it. Rays go through what is drawn, every part's real boxes and meshes in the rest pose,
wheels and turrets included (`VehiclePlan.PartTriangles`, the triangles `VehicleChecks.Gaps` shoots through, each tagged with its part), so a drum behind a bed wall, a
crate in a room's magazine or an antenna under a rack counts as covered. `Min` of the picked parts must pass; `Optional`: none there doesn't count against it. The rays
are a geometric test: they don't know a part's health, armour or what a round does on the way, and a hit on a part's own other face counts as a hit.

**Guaranteed marks (`Vehicle.GuaranteeMarks`, `VehicleMarks.cs`).** Kit 2 doesn't leave a mark to chance: after the dressing (`Trim`), every mark of the signature that
applies and that the vehicle has too few parts of (`VehicleIdentity.Lacking`, by name; a mark picked by role, the Bandit's fuel in the open, is the shape's to carry) gets
one, as the Pipe Box pass stands its box. Each mark rolls on its own fork of the seed (`"mark " + label`), so nothing else of a seed moves. Its parts are the mark's names
the style lists in its `mast`, `spike`, `gear` and `trophy` slots, as often as it lists them, then the rest of the names; it tries them on a ranked list of spots:

1. up-facing free sockets on a room's roof, the cab shell, the hood, a roof rack, the bed, the porch;
2. a post: half a metre of the frame's beam (in the frame's paint) on the rearmost free top socket of the body's frame and plates, the mark on its tip: where a War Bus
   whose roof sockets all went to turrets still has room;
3. sideways on the headboard, the flanks, a room's walls, the cab and the hood's sides.

Every spot is tested before anything goes on, since nothing placed is taken off again: an upright mark wants open sky over its footprint (not under a rack, a deck or an
overhang, so the antenna or flag weak point is hittable), stays out of every turret's sweep (lower than the ring's top, or further from it than the longest gun reaches
or the gunner's arm, 1.2 m), and a post wants room for itself and the mark. There is no bracket on a turret ring: the ring's fixed side sockets sit inside the sweep of the
gunner's arm and seat. A mark with nowhere to go is left missing and the gate below re-plans as before. The pass runs on every kit-2 plan, before the stability and fill
checks, and on the seeds of the kit-1-only shapes (Technical, Fuel Trailer, Tanker Rig) that ship nowhere (`VehicleRegistry.Make`: not in the generator's
`Seeds`, or with `--kit 2`), so a dynamic Technical carries its mast too while every committed file stays as it is. `--identity ... --raw` lists what it placed
(`marks placed: flags, beacons or brollies: a Belisha Beacon on the Cab Over Shell`).

**In generation.** `ShapeGrammar.Make` hands each built plan to `VehicleIdentity.Gate`. Under the threshold or with a mark missing, the seed plans again from the next fork
(logged: `[vehicles] IT War Bus 1: plan 1 reads 0.78 as IT (no mast or dish): ...; planning again`); the second plan is kept if it is better, else the first, so identity
costs at most one more plan (a shape that can't carry a mark would otherwise pay for all four). If every later plan fails as it is built, the best plan scored is kept.
Kit 1, the committed seeds included, is never re-planned: it is only reported. `--no-identity-gate` turns the re-plan off.

**Against every style.** `--identity` and the quality tools score each vehicle against every style in `Styles.All` and report any that reads more as another style than its own.
Bandit is nearly always the runner-up: many styles start from the Bandit slots, every vehicle carries its fuel in the open, and an Iron Spike is a Bandit mark.

**Measured** (6 seeds of every shape a style ships, generation only, 2026-11-06; kit 1 for the Technical, the trailers, the Tanker Rig and the Double Decker, kit 2 for the
rest; `--kit-report --seeds 6`):

| Style | Mean identity | Weak points all pass | Read as another style | Kit 2 with every mark, gate off / on | Closest margin to another style |
| --- | --- | --- | --- | --- | --- |
| Bandit | 0.94 | 47/47 | 0 | 22/22 / 22/22 | 0.23 (Militia) |
| IT | 0.89 | 35/48 | 0 | 19/26 / 22/26 | 0.11 (Bandit) |
| Call Centre | 0.89 | 35/48 | 0 | 14/23 / 20/23 | 0.07 (Bandit) |
| Highland | 0.96 | 48/48 | 0 | 20/20 / 20/20 | 0.18 (Bandit) |
| British | 0.89 | 52/52 | 0 | 13/24 / 15/24 | 0.10 (Bandit) |
| Militia | 0.91 | 48/48 | 0 | 30/30 / 30/30 | 0.10 (Bandit) |

What it found: the IT and Call Centre vehicles carry no mast or dish on 16 and 19 of 42 vehicles with a seat, the most on the Technical (kit 1: one in six has one) and the
War Bus (kit 2: its roof's sockets go to turrets first, and a re-plan doesn't always help: 2 of the 5 kit-2 IT seeds measured still have none); the British Scrap Tanker often has neither a flag nor
anything for tea (no room, no cab shell to dress). Those were a generator's job, not the score's: the guaranteed marks pass above. Tyres and fuel are
hittable on every vehicle; the only covered weak points seen are the rounds in a room's magazine, which is why the Bandit's "ammo crate" applies to light vehicles only.
Generation with the gate cost IT and the Call Centre about a fifth more over all their shapes (a War Bus 1.2 s on average, 2.3 s at worst), the rest nothing measurable.
The silhouette range is the same for every style: outlines come from the shapes, which every style shares, and the styles' 5th to 95th percentiles agreed to a few points,
so for now the range keeps a style off a box or a sliver rather than telling one from another.

**With guaranteed marks** (the same 6 seeds of every shape, `--identity STYLE --seeds 6`, 2026-11-06):

| Style | Kit 2 with every mark, before / after | Identity re-plans (kit 2) | Mean identity | Weak points all pass | Dynamic kit-1 Technicals with every mark (seeds 30 to 37) |
| --- | --- | --- | --- | --- | --- |
| IT | 22/26 / 26/26 | 7 / 0 | 0.89 / 0.93 | 35/48 / 42/48 | 7/7 |
| Call Centre | 20/23 / 23/23 | 9 / 0 | 0.89 / 0.93 | 35/48 / 43/48 | 6/7 |
| British | 15/24 / 24/24 | 11 / 0 | 0.89 / 0.95 | 52/52 / 52/52 | 7/7 |

Every vehicle still short of a mark is a committed kit-1 seed (the IT and Call Centre War Bus 6, Scrap Tanker 1, Gun Wagon 1 and their committed Technicals, the
British Box Truck 1, Double Decker 1 and 5), which the pass never touches; the antenna weak points that fail are those seeds' missing antennas, and every mark the pass
placed is hittable. The one dynamic Technical without a mark (Call Centre 32) has its only free spots under the gun's sweep. With no identity re-plans left, a kit-2 IT War
Bus took 0.78 s on average and 1.2 s at worst in a run as quiet as the one before (its kit-1 shapes took the same time; 1.2 s and 2.2 s before), the Call Centre's
0.60 s and 1.1 s (0.95 s and 1.8 s); later runs with other builds on the 2-core box read up to 1.3 s on average for both kits alike, so read the re-plan count as the cost.
