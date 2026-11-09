# Body styles: a proposal (vehicle kit)

The user's request (2026-11-07): "Investigate new body styles. Currently the factions' vehicles are fairly similar in style. New pieces potentially, but more
specifically, the lines of the vehicle have very cookie-cutter pieces. All of the enclosed spaces are only one of three varieties. Considering how many vehicles we're
going to implement, I want this feature to be good on the toolkit before I move forward with creating more vehicles." Earlier reviews said the same from other sides:
"still too boxy overall, and rounding corners won't fix that", "few slopes and bends; the kit needs vehicle-appropriate contour and body pieces; make more parts if
needed", "interiors too alike", "cabs too alike: no roll cages, just a flat wall".

**Status (2026-11-08): phases 1 to 4 are built.** Phase 4 (section 10): each faction's exterior on its rooms and cabs (the user's note of 2026-11-07: "the
enclosed section of the vehicles is still looking very similar between factions... I want factions to have more distinct visual looks"): trim cut for each bay's
side (welded plates and cage bars, slat armour and stowage bins, a fairing and vents, signboards, clinker planks and a strake, a livery waistband and chrome),
fittings on cabs and hoods (a roof sign, a destination board, a radome, a mascot and a chrome grille, a roof basket, jerry cans), capsule side glass, Militia's own
dressing, and an exterior measure in `--body-report`. Phase 3 (section 9): cabs and hoods are lofted in the body's language, with six cab kinds weighed per
faction, window panes as glass parts, and each faction's cab proportions as data (the user's note of 2026-11-07: "factions should look more distinct"). Phase 1 (section 6): kit 5 is the current kit, every committed faction vehicle was made again by it, the tools
see sections, and the user's decisions are in section 5. Phase 2 (section 7): the fixed limits are data (scale classes), proportions, rooflines along the length, doors
on either side or in the back, ride height bands, turret spots sized from the parts, paint by runs and rows; the committed vehicles were made again. Section 8 is how
a later archetype is added as data. Sections 1 to 4 are the investigation as it was written, before the build.

This file is the investigation: what the kit makes today (with numbers), every way found to break the sameness with what each costs, a working prototype of the first
one (lofted hull bays in a body language per style, `--kit 5`), a recommended plan, and the questions the user should answer before the big build. Pictures for the
decision are in `/home/claude/vk-bodies-sheets/` on the agent box (not committed): see "Pictures".

## 1. Audit: what the kit can make today

### Every enclosed or bodied space

| Space | Where it comes from | Varieties | What varies by seed |
| --- | --- | --- | --- |
| Walk-in room (box truck, war bus, bunker, crew room) | `Vehicle.Hull`: a Bolt Deck floor and a run of faceted bays from `Constructs/Parts/Hull` (`tools/constructs/hull_parts.py`) | **3 cross-sections** (T tall 2.3 m, L low 2.02 m, N narrow 2.12 m), 9 bays between them, 3 brows, 3 tails, 1 door bay: 16 parts | which section at each metre (the roofline's steps), length 3.5 to 7.5 m in whole metres, where the door bay is |
| Cab | one shell part on the seat sill (`OpenCab`, style's `heavyCab`/`cab`) | 7 shells (Roll Bar, Roll Cage, Mesh Guard Cage, Tin Roof Cab, Chop Top Cab, Slit Cab, Windscreen Frame) + the Cab Over Shell | which shell; nothing about its size or shape |
| Nose | `Hood()`: one hood part, grille, fenders | Long Hood, Bull Hood, Snub Hood, Slab Nose, "plates"; 4 grilles/rams; 2 fenders | which part |
| Ammo hutch | one part (Ammo Hutch) | 1 | none |
| Open bed, porch, gun deck, rack | `Deck` plates | flat; sides are walls, tube rails or nothing | length, plates, the side kind |
| Tank | Tanker Barrel 2 m / 3 m | 2 | which length |
| Flanks | Hull Pannier 1 m, Gun Sponson, Hull Skirt 1 m | 1 of each | where they hang |

So "only one of three varieties" is exact: every walk-in space on every faction's vehicle is made of the same three cross-sections, and every room is 2.2 m wide with
its door 1.5 m into the +x side. A cab is one fixed part, a hood one fixed part.

### What the sections are

All three sections are one profile at three sizes: a floor edge, a waist that leans out 8 cm at 0.7 to 0.85 m, a shoulder, a 45-degree-ish cant and a flat roof. Brows
and tails are the same profile shrunk to 75 % of the height and 93 % of the width, so every room has the same rake at both ends. Every style uses all of them with the
same weights give or take (`LayerTables.Body`: Tall 3, Low 2, Narrow 0.5 to 2), so from the front a Bandit war bus and a British one are the same shape in another
paint. The style changes the paint, the dressing and which cab/hood part; the room, which is most of a heavy vehicle's outline, never changes.

### Parts that exist and are never used

Counted over the 123 committed generated blueprints (all kit 1) and the kit's source (kits 2 to 4 reference the same parts):

| Part | Committed vehicles using it | Referenced by the kit at all |
| --- | --- | --- |
| Hull Bay L-N, Hull Bay N, Hull Bay N-L | 0 | yes (the grammar can roll them; kit 3 sometimes does) |
| Hull Bay T-N 3, Brow N 5, Tail N 3 | rare | yes |
| Armour Slope 1 m, Armour Slope 2 m | 0 | **no** |
| Slant Sheet 1 m, Slant Sheet 2 m, Roof Slant 1 m | 0 | **no** |
| Skirt Sheet 1 m, Skirt Sheet 2 m | 0 | **no** |
| Tube Bend 0.25 m, Tube Bend 0.5 m | 0 | **no** |
| Bed Rail 2 m, Chassis Tub 1 m, Scrap Bunk | 0 | only as a slot entry |

Seven contour pieces (slopes, slants, skirts, bends) were made and never wired into the kit: the cheapest new lines are already in the part library.

### Why it reads as cookie-cutter (the causes, ranked)

1. **One cross-section family for every style.** Measured below: from the front, rooms of different styles are as alike as rooms of the same style.
2. **Fixed proportions.** Room width 2.2 m always; heights 2.02 / 2.12 / 2.3 m only; floor height from the gear recipe only; the cab always at the same station; the body always starts 0.2 m behind the cab. The grammar varies *order* and *length*, not *proportion*.
3. **The room is a tube.** A run of bays with one section each: no taper in plan, no curve in the roofline (it can only step between three heights), no wedge nose, no kick-up, no overhang, no split level.
4. **Single-part cabs and hoods.** A cab is one of seven parts, the same size on every chassis; a wall of hull bay stands behind it. The reviews' "no roll cages, just a flat wall" is a cab shell in front of a brow.
5. **Paint washes form out.** The Bandit wear layer makes most panels one rust brown, so faceting barely shows at a distance (see the pictures: kit 3 and kit 5 sheets look alike from 20 m because the colour is uniform; up close and from the rear the sections differ clearly).
6. **The metric can't see it.** `VehicleDiversity`'s silhouette term is the side outline and the plan width along the length; nothing measures the cross-section or the outline from the front, so a change of section scores zero and a tool run can't tell a styled body from today's.

### Measured (2026-11-07, the 2-core box, generation only)

`--body-report all --seeds 4` (new; every grammar shape of every style, seeds 1 to 4) measures a room's cross-section from the front: its half width in 12 bands of its
own height, each over its widest, and the mean difference between two vehicles (0 the same section, about 0.3 for a box against a triangle).

| | kit 3 (today) | kit 5 (prototype) |
| --- | --- | --- |
| distinct room sections over 117 vehicles | 13 at most a style (the same 12 shared by all) | 41 to 47 a style, none shared between styles |
| distinct bay parts a style | 10 to 13 | 46 to 52 |
| front profile, mean pair within a style | 0.024 to 0.037 | 0.039 to 0.066 |
| front profile, mean pair between two styles | 0.025 to 0.034 (**the same as within**) | 0.054 to 0.157 (Call Centre against Militia 0.157; IT against British 0.054, the closest: both rounded) |
| generation failures (every grammar shape, 4 seeds, 6 styles) | 1 of 117 | 1 of 117 (the same Bandit Scrap Tanker 3, not a room) |
| generation, mean a vehicle | 420 to 1650 ms | 340 to 620 ms (noise: other agents' builds) |
| parts a vehicle, mean | 125 to 136 | 125 to 135 (a lofted bay is one part, as a fixed one) |

`--diversity-style` (the existing metric) moved by at most 0.01 on any shape between kit 3 and kit 5 (Bandit mean pair 0.311 against 0.310): it is blind to the
change, which is cause 6 above. The full check (`--sweep-vehicle --seeds 6 --kit 5`, the drive, guns, rooms walked, rays for holes, coplanar faces) passed 70 of 72
vehicles over twelve shapes and five styles; the two failures are listed under "The prototype".

## 2. The options

Each option says what it is, what it changes for a player, what it costs in agent time (one agent, Opus, on this box: builds take 1 to 3 minutes, a sweep of 6
seeds 1 to 2 minutes), its risks, and which rules it touches. The rules every option keeps: walkable rooms with a door (headroom 1.8 m, the walker), no holes
(`VehicleChecks.Gaps`), no coplanar faces, colliders and part counts sane, salvageable per part, the drive check, identity, determinism (`v.Dice` forks), and the
8 ms tick (generation is sliced; the user allows slower generation since spawns happen out of view).

### A. Parametric cross-sections at run time (prototyped)

A hull bay made at run time for any section: `HullLoft` (`Source/Constructs/HullLoft.cs`), a part named by its key, like an Ordnance Rack's shapes:
`Hull Loft (100 104,0/107,169/80,202/0,202 104,0/107,185/80,221/0,221)` (length in cm; a section is a half profile in cm from the floor edge up and over to the
centreline; flags `d` door, `f` front cap, `r` back cap). Any machine makes the same part from the name, so blueprints, saves and the wire carry only the name
(protocol 194: a new kind after `Extrusion.WireMark`, the key as text). Facets are drawn "hull" shapes, hidden slabs just inside the skin stand for them to the
build rules and players, sockets are where the fixed bays have them (feet on the Bolt Deck grid, waist pads, top, lever).

- For a player: rooms of any shape the language allows; every one walkable, salvageable as a part.
- Cost: done as a prototype (about 500 lines). To ship: 0.5 day (an Assembling recipe or salvage rule for run-time parts, the inventory's handling of a salvaged loft bay, a save round trip, a network test with a client), plus the decisions below.
- Risks: run-time part ids are per machine (handled as extrusions are); the run-time table holds 48,000 defs a run (a vehicle uses 5 to 9 bays, and keys repeat, so thousands of vehicles); `ConvexHull.Of` drops one of two near-twin faces (found here: a twisted panel within a millimetre of flat lost a triangle and the rays found the hole; panels are now split into flat triangles when not exactly flat).
- Rules: it is one part a bay as before, so part counts and colliders are unchanged.

### B. Lofted bodies (prototyped in part)

A body as a sequence of sections along its length, each bay lofted between two: taper in plan, rake, kick-up, wedge noses, boat tails. The prototype lofts brows
and tails by a seed's rake (each end its own: brow 0.55 to 0.9 of the height, tail 0.6 to 0.92, narrowing to 0.8 to 0.97 of the width) and steps between tall, low
and narrow sections as kit 3 plans them. Not yet done, and what each needs:

| Lofting move | What it needs | Cost |
| --- | --- | --- |
| a **curved roofline** (each bound its own height, easing over two or three bays: a hog or a sag) | the grammar's turret spots and the roof rack judged by the real roof, not by plan names ("Bay T"); the rack's deck on a sloped roof | 0.5 day |
| a **tapered plan** (the body narrows to the tail, or widens behind the cab) | furniture and seats placed by the real wall, not `BayHalf`; the deck under it trimmed | 1 day |
| a **wedge nose** on the room (the brow 2 to 3 m long, low at the front) | brows longer than a metre: the magazine and the first seat move back | 0.5 day |
| a **kick-up** (the floor rises over the rear axle, a step inside) | a floor at two heights: the walker, the deck, a step; the door bay on the low part | 1.5 days |
| **overhangs** (a body wider than its floor above the wheels, a box on a box) | the 0.12 m band under walls (wheels' tops), sockets for panniers on the upper box | 1 day |

### C. New enclosed-space types

Each is a module in `ShapeGrammar.Catalogue` plus its build; most reuse A and B.

| Type | What it is | Cost | Notes |
| --- | --- | --- | --- |
| **crew cab** | a short room (2 to 2.5 m) with seats in two rows, in place of the cab shell: a pickup's cab, a crew truck | 1 day | needs the driver's module and wiring inside a room; solves "cabs too alike" for the enclosed styles |
| **cab-over capsule** | the cab lofted in the body's language, flowing into the room (one mass, a stepped front) | 1 day | a new cab module; the Cab Over Shell stays for the others |
| **open-frame / caged room** | a room of beams and mesh (the Bandit cage), walkable, the gunners visible | 1.5 days | rays find holes by design: the Gaps check needs a "caged" room kind that only checks the floor and the door |
| **boxy van** | a tall square room with a flat roof and a split rear door | 0.5 day | a language plus a rear door (B) |
| **rounded bus** | the bus language with a long raked front over the cab (the cab inside the room) | 1.5 days | the driver inside the room: OpenCab in a room, the windscreen as glass facets |
| **low-slung hull** | a room 1.9 m inside between swing-arm wheels, the floor at 0.35 m | 0.5 day | the low-floor gear exists; the headroom check is the limit |
| **tower or turret house** | a narrow tall room on a room (a commander's cupola), a ladder inside | 2 days | two floors: the walker between levels, a ladder part inside |
| **split rooms** | two rooms on one chassis with a gap or a bed between (a front crew room, a rear hold) | 1 day | the grammar allows one room a vehicle today ("two rooms or two beds" throws): tags per room |
| **raised deck over a room** | the Double Decker's top deck on any room, with rails | 0.5 day | exists for one shape; generalise |
| **side pods** | sponson rooms hung on a body (walk-in pods, 1 m deep) | 2 days | rooms off the main deck; doors between rooms |
| **double deck** | two walkable floors | 3 days | stairs or a ladder, two ceilings, headroom both; the hardest |

### D. Per-style body language (prototyped)

Each style speaks one or more languages (`BodyLanguage.ByStyle`, `Source/VehicleKit/VehicleBodyLanguage.cs`), each a tall section and how a seed varies it
(height range, brow and tail rake, how narrow the ends get). The prototype's nine and who speaks them:

| Language | Shape | Styles (weight) |
| --- | --- | --- |
| slab | welded slabs: upright walls, one hard chamfer, a flat roof | Bandit (2), Highland (0.5) |
| ledge | a box welded over a box: the upper walls stand proud on a ledge | Bandit (1.5) |
| armour | a flared lower hull, a steep glacis-like upper, a narrow roof | Bandit (0.5), Militia (1) |
| pod | clean rounded panels: tumblehome and a crowned roof | IT (2) |
| van | near-upright sides rounding into a gently crowned roof | IT (1.5), Call Centre (1) |
| cabin | a site cabin: plain walls, eaves and a pitched roof | Call Centre (2) |
| bus | tall upright sides, a domed roof with rounded corners | British (2.5) |
| coach | a lower body, a long tumblehome, a raked front | British (1.5) |
| boat | a flare to a strake, a curved tumblehome, a boat-tail stern | Highland (3) |

A style's language should reach beyond the room's section; the full proposal for each style:

| Style | Section | Proportions | Plan and ends | Cab and nose | Flanks and contour |
| --- | --- | --- | --- | --- | --- |
| Bandit | slab, ledge, armour; asymmetric allowed (one side a ledge) | tall and short, overhangs | blunt, raked brow, open caged rear | cages, slit cab, plated hood, ram | welded patches, slanted armour (Armour Slope), skirts with gaps |
| IT | pod, van | long, low, even | tapered tail, smooth wedge nose | cab-over capsule in the same panels | flush skirts, no panniers, roof dish fairing |
| Call Centre | cabin, van | boxy portacabin on a chassis | square ends, eaves | short hood, enclosed cab | signage boards, awning over the door |
| Highland | boat | long, low sheer line rising to the bow | boat-tail stern, raised bow | long hood, caged cab | strakes along the waist, clinker ribs |
| British | bus, coach | tall, upright, double deck | rounded front and rear domes | cab inside the bus front | skirt panels to the ground, chrome rubbing strip |
| Military (Militia, future) | armour | low, wide, flat-faceted | glacis nose, sloped tail plate | slit cab or none (driver in the hull) | track-style skirts, stowage bins |
| High-tech (future) | a smooth many-faceted pod | very low, long | teardrop plan | canopy | no greebles: smooth fairings over the wheels |

Cost: the section table is done; adding proportions per style is a `BodyRange` row each (0.5 day for all); the cab, nose and flank columns are options C, E and F.

### E. New contour pieces

Parts that make lines and are needed by the languages: wheel arches that follow a section (a fender per language), side skirts (the 7 unused contour parts above
first), chamfer strips along a room's roof edge, ribs and strakes (Highland), louvres and vents (IT, Military), rubbing strips (British), an awning (Call Centre),
roof fairings over turrets and dishes. Each family is a generator in `tools/constructs/` (with `zfight.py`), recipes in Assembling, and a `Dress` slot or a
language rule that places it. Cost: 0.5 day a family (generator, recipes, placement, a sweep), about 3 days for the lot. Risk: coplanar faces with the lofted skin
(dressing goes on with `clear: true` as now).

### F. Cabs and noses from the same loft

A cab as a lofted shell in the body's language (a short open-backed loft with glass facets, its own sections), and a hood as a loft (a nose section to a cowl
section) with the engine under it: every vehicle's nose and cab then follow its body. This is the reviews' "cabs too alike" and "few bends". Cost: 2 days (the cab
shell's sockets for seats and wiring, glass, the hood's clearance over every engine's details, which already poke through Long and Snub Hoods by 5 to 6 cm).

### G. Interiors that follow the body

Rooms that are not a tube give interiors a shape: a narrow brow is a cockpit, a tall bay a gun loft, a tail a hold behind a bulkhead, two rooms a crew room and a
hold. Today's layouts (bunkroom, workshop, server room, call floor...) are 4 lists of furniture a style down two walls. With B and C: a layout per room kind,
furniture against the real wall (a sloped wall takes low furniture), bulkheads with doorways between rooms. Cost: 1.5 days after B.

### H. Proportion ranges

The cheapest change for the most variety, independent of the rest: room width (2.0 to 2.6 m: the floor patch is already seeded), floor height (more spread in the
gear), cab station, gap length, body height against the cab's. Each needs only the deck, the furniture's reach and the turret spots to follow the width. Cost:
1 day. Risk: stability (a tall wide room is heavier and higher): the grammar already plans again under 0.63.

### I. Measuring what the user sees

Before more variety, make the tools see it: a front-view term in `VehicleDiversity` (the cross-section signature `--body-report` computes), the section's identity
in `StyleSignature.Silhouette` (each style's mean front profile; today the silhouette range is the same for every style), and the contact sheets' camera closer
with a rear three-quarter view (the current sheet's views are too far to show a section). Cost: 0.5 day.

### J. Paint that shows form

Paint by row (lower hull, upper hull, roof) and by panel edge instead of by part, so faceting reads at a distance: the Bandit wear layer currently turns most rooms
one brown. Cost: 0.5 day (wear and `PaintAll` rules; a lofted bay already has three colour rows).

## 3. The prototype (kit 5; as it was before phase 1, section 6 has what changed)

**What it is.** `VehicleKitVersion.Lofted = 5`: kit 3 (or 4) with each room lofted in its style's body language. Only `--kit 5` makes it; `Current` stays kit 3,
so `--gen-vehicles` still reports every committed file identical (checked on this branch), and the dynamic spawn is unchanged. The language is rolled on the body
layer's `"language"` fork (`BodyLook.Roll`), so nothing else of a seed moves: kit 5 seed 7 is kit 3 seed 7's plan, guns and chassis with another room.

**Files.** `Source/Constructs/HullLoft.cs` (the run-time bay), `Source/VehicleKit/VehicleBodyLanguage.cs` (languages, `BodyLook`, `--body-report`). Small hooks:
`ConstructParts.Find` (the prefix), `IsNamedRuntime`/`RuntimeMade`, `Extrusion.Write/Read` (wire kind 245; 246 is the material painter), `ProtocolVersion` 196, `Vehicle.Hull` (names from the
look), `Room.Plan` (the plan's bay names, which the roof rack now reads instead of part names), the grammar's roof-turret candidates (no turret on a lofted tail: its
roof slopes), `VehicleVariety.Kind` (a lofted bay counts as a bay, door bay, brow or tail kind), `Program.cs` (`--body-report`, `--language`).

**Commands.** `--kit 5` on any vehicle tool; `--language slab` (any of the nine) puts every kit-5 body in one language, to compare them on one plan;
`--body-report STYLE|all [--seeds N] [--from S]`.

**Rules kept, and how.** Every tall section keeps x >= 1.02 m below 1.6 m (furniture reaches a metre: `Vehicle.BayHalf`), stands on the floor at 1.0 m or more, and a
low one keeps 2.02 m at the centre; end sections never narrower than 0.76 m (a seat stands half a metre out); the door bay is the tall section with the fixed
door frame at x = 1.0 m to 2.0 m and a cant to the section, its ends closed where a tumblehome comes in under the doorway's top; hidden slabs stand 1 to 5 cm inside
the skin (a sponson or a pannier stands against the skin), slanted rows in steps of 30 cm; the two halves of a roof overlap a centimetre at the centreline (a ray
straight up the middle hit the seam); the waist (sponsons, panniers) is the widest height from 0.4 to 1.5 m, nearest 0.85 m among those within 3 cm.

**Found on the way.**
- `ConvexHull.Of` drops a face whose normal is within 1e-6 of one it has: a twisted panel (front and back sections a little different) less than a millimetre
  from flat lost one outer triangle, and the room had a hole the rays found. `HullLoft` splits any panel not exactly flat into two flat triangles. Any generator
  that lofts between two sections must do the same (hull_parts.py's T-L bays are twisted too; none failed, but nothing guards them).
- The z-fighting check skips "hull" shapes entirely (`ZFight.Boxes`: `Shape != 0` is skipped), so no faceted part's faces are checked against anything. Lofted bays
  meet edge to edge with their end faces inside one another (not drawn), so this is fine for them, but dressing on a faceted skin is unchecked.
- The worst Bandit generations took 23 to 35 s in both kits in some runs (many plans, a loaded box) and 5.8 s in a later kit-5 run: not a body issue, but worth a look before generation gets slower on purpose.

**What it shows** (pictures below). Up close and from the rear, the languages read as different vehicles: a gabled cabin, a domed bus, a pod, a ledged slab. From the
side at the contact sheet's distance, and under the Bandit wear's single brown, the difference is small: the outline from the side is still the plan's stepped
roofline and the same proportions. **Sections alone don't fix "cookie-cutter"**; they are the foundation the other options build on (B's curved rooflines and
tapers, F's cabs and noses in the same language, H's proportions, J's paint).

**Known failures** (full check, 70 of 72 pass): Bandit War Bus 5 kit 5 (a sponson on a ledge bay: its turret meets the overhang above the waist) and Scam War Bus
5 kit 5 (a roof turret's hinge against the cabin's gable). Both are turret spots judged by plan names, not the real roof: option B's first row.

**Not done in the prototype**: salvaging a lofted bay into the inventory and rebuilding it, an Assembling recipe for one, a save round trip, a client over the
network (the wire path is written and builds, nothing has sent one), kit 5 with a role (`--role` makes kit 4: the role path forces kit 4), the Double Decker
(Karl's generator calls `Hull` without a look, so it keeps fixed bays).

## 4. Recommended plan

Order by what the user sees per day, each phase a sweep and a contact sheet for the user's grade before the next.

| Phase | What | Options | Agent time |
| --- | --- | --- | --- |
| 1 | Make the tools see it: front-profile term in the diversity distance, each style's section in its identity signature, closer sheets with a rear view | I | 0.5 day |
| 2 | Ship lofted rooms: kit 5 becomes current (regenerate the committed seeds through the generator), salvage, recipes, saves, network test; turret spots and the rack by the real roof | A, D, B (roof) | 1.5 days |
| 3 | Proportions and a curved roofline per style; paint by row | H, B (curve), J | 2 days |
| 4 | Cabs and noses lofted in the body's language; crew cab; cab-over capsule | F, C (crew cab, capsule) | 3 days |
| 5 | Contour pieces: the 7 unused parts wired in first, then fenders per language, skirts, strakes, ribs, awnings | E | 3 days |
| 6 | New spaces: caged room, split rooms, raised deck on any room, low-slung hull, boxy van | C | 3.5 days |
| 7 | Interiors that follow the room; bulkheads | G | 1.5 days |
| 8 (later) | Taper, wedge noses, kick-ups, overhangs; tower house; side pods; double deck | B, C | 6 to 8 days |

About 15 agent-days to phase 7. Phases 1 and 2 are the precondition for the user's next round of vehicles; 3 and 4 are where most of the "each faction looks like
itself" comes from.

## 5. Open questions for the user

**Answered (the user, 2026-11-07):** 1. smooth for IT, British and future high-tech (8 to 12 facets a side), flat-faceted for Bandit and Militia (3 to 6); 2. Bandits
mix their shapes per bay, other styles keep one per vehicle; 3. rooms may be non-rectangular inside (later); 4. cabs may merge into the body for some styles (later);
5. kit 5 becomes the default and every committed faction vehicle is made again by it; 6. a salvaged bay is the shaped section itself, buildable on players' own
vehicles; 7 to 9: new spaces, no room size limit, Bandit paint by rows (later); 10. the front profile joins diversity and identity at a moderate weight. The
questions as they were asked:

1. **How far toward smooth?** The prototype's sections are 3 to 6 facets a side (visibly faceted, like the parts around them). Should IT, British and high-tech bodies
   be smoother (8 to 12 facets: costs boxes and drawing) or stay faceted to match the rest of the game?
2. **Which styles get which language?** The table in D is a first guess: is the Call Centre a site cabin? Is Highland boat-like? Should Bandits mix languages on one
   vehicle (one side a ledge, patched slabs), or keep one per vehicle?
3. **May rooms be non-rectangular inside?** Tapered or wedge rooms mean furniture against sloped walls and a narrower aisle; kick-ups mean a step inside. Yes, or
   keep the inside a 2 m box and shape only the outside?
4. **Cabs: inside the body or separate?** The silhouette rules say a cab is its own mass. Lofting the cab in the body's language could merge them (a bus front, a
   capsule): is that allowed for some styles (British bus, IT), or must every cab stand apart?
5. **Kit 5 for every new seed, and regenerate the committed ones?** Continuity isn't required; regenerating changes all 123 committed files (they stay kit 1 today).
   Or keep the committed seeds as they are and make kit 5 current only for new and dynamic seeds?
6. **Run-time parts in players' hands.** A salvaged lofted bay is a part no file has: should players be able to build with it (it goes in the inventory under its
   key, as an Ordnance Rack's shape does), or should salvage break it into steel?
7. **Which new spaces matter most?** Crew cab, caged room, split rooms, raised deck, low-slung hull, tower, side pods, double deck: which three first?
8. **Proportions: how big may a room get?** Width up to 2.6 m changes how vehicles sit on roads and in convoys; height over 2.5 m changes stability. Limits?
9. **Paint.** Should Bandit wear keep turning most panels one colour (it hides the shapes), or should rust follow rows and edges so the form reads?
10. **The metric.** Should the front profile join the diversity distance with its own weight (say 0.15, taken from parts), and should "reads as its style" include the
    section (a pod on a Bandit truck would then lose identity)?

## 6. Phase 1, as built (2026-11-07)

**Kit 5 is current** (`VehicleKitVersion.Current = Lofted`) for every path: a shape's seed, the dynamic spawn, and a role's, family's, convoy's and context's
vehicle (kit 4's role on lofted rooms; `--kit 4` still makes the fixed-bay role vehicle, `--kit 3` the fixed-bay kit). The language is rolled on the body
layer, so a family's marks share theirs. Shapes without a recipe (Technical, Fuel Trailer, Tanker Rig, Double Decker) stay kit 1: the Double Decker (Karl's
generator) keeps its fixed bays.

**Languages per faction** (`BodyStyleRule`, VehicleBodyLanguage.cs; data, a row per style): Bandit slab, ledge and armour, faceted as written (3 to 5 facets a
side), each station between two bays a new language half the time (cobbled from wrecks: a bay lofts from one language's section to another's, the fewer-pointed one
split along its longest side rows so the shapes match point for point); IT pod and van and British bus and coach drawn smooth, 8 to 12 facets a side by the seed
(the control polygon cut at its corners three times, then walked at even steps; the last row stays level, so the roof is one panel); Scam, Highland and Militia
(armour) as written, one language a vehicle. A room whose stations mix says so in its kit line (`sections slab+ledge+armour`).

**Turret and gear spots from the real bays** (`BaySpots`): a roof turret only on a bay whose roof is level along it, as tall as the room's tallest and with no
neighbour higher (a gable or a crown falling away under the turntable is fine); a side gun only where nothing (an eave, a ledge, a lower neighbour's roof edge)
stands out more than 3 cm over the waist from 0.15 to 1.3 m above it, on the bay and its neighbours; a pannier only with 0.35 m clear above it; a roof rack only on
roofs flat to 0.55 m either side. The two kit-5 failures of the prototype were side guns, not roof guns: Scam War Bus 5's sponson turret ran under a lower bay's
roof edge, and a ledge's waist landed on its slanted step, so the sponson's back sat in the wall above; a waist now sits on an upright stretch of wall.

**The part** (`HullLoft`): sections up to 16 points, in a compact key (`~` and three characters a point, `=` for a back section that is the front's: protocol 198),
so a 13-point smooth bay's name stays under 128 characters (the first prototype's text keys still read). Facets are mitred where they meet (no overlap whose end
faces share a plane), a pitched roof's halves meet on a mitred ridge with a strip over it (a ray straight up the middle passed along the seam), caps stand 3 mm off
the floor. A spawn hands back to the tick after each new bay is made.

**Salvage.** A lofted bay is a welder part, so physics-only ("structural"): taken off a vehicle (the attach tool, survival or creative) it is cut free as a body of
its own holding that exact part, by its key; the attach tool builds it onto anything, like any hull bay. Checked in a throwaway harness on a host and a joined client
(30 ms lag, 8 ms jitter, 2 % loss), Bandit War Bus 1: the client's truck had the same bay by its key; the bay salvaged by the client's own Remove message came off as
a body (owner the player) the client saw by its key; it went onto a player's Bolt Deck 1x2 m by the build rules and the client saw that too; a paste of that
construct kept it on host and client, a blueprint saved from it built it back, and a world save read it back; no packet dropped. Recycling gives by mass and class as
for any panel (a 148 kg brow: 74 metal; the fixed T bay, 120 kg: 60). There is no Assembling recipe for a lofted bay: a player gets one only by salvage (decision 6).

**Tools see sections** (`FrontProfile`: half width in 12 bands of the thing's own height; edge-sampled, so a fixed T, L and N bay, one profile at three sizes,
now read alike, 0.001, where the first report's vertex sampling gave 0.03 of noise). `VehicleDiversity` has a front term (weight 0.15, from silhouette 0.05, parts
0.05 and layout 0.05): the rooms' and the whole body's profile, the mean difference times 3. `VehicleIdentity` has a section part (0.10, from palette 0.05 and
silhouette 0.05): each room bay's two sections against the nearest of the style's languages. `--no-sections` measures both as they were.

### Measured (2-core box, loaded by other agents: times are noisy)

| | before: kit 3 | after: kit 5 |
| --- | --- | --- |
| full check (`--sweep-vehicle --seeds 6`), every style (6, Militia included) x 5 recipe shapes | 178 of 180 | 178 of 180 |
| the failures | Bandit Scrap Tanker 3 (no socket takes the barrel), British Flatbed Fort 1 (a Firebreather 22 degrees off its gunner's look) | Bandit Scrap Tanker 3 (the same), British War Bus 6 (a Firebreather 23 degrees off) |
| the 75 committed kit-5 seeds, full check | - | 75 of 75 |
| parts a vehicle, mean over the sweeps | 133 | 129 |
| generation (sweep's wall clock, mean of shape means) | 1968 ms | 1465 ms |
| sweeps' CPU (user), all 30 | 482 s | 372 s |
| room sections from the front, mean pair within a style (`--body-report`, 6 seeds) | 0.001 (every style the same profile) | 0.009 (Militia) to 0.027 (Scam) |
| ... between two styles | 0.001 | 0.020 (IT against British, both smooth) to 0.078 (Scam against Militia) |
| distinct bay parts a style | 11 to 14 | 70 to 78 |
| facets a side (`--body-report`) | 3 (fixed) | Bandit 3 to 5 (mean 4.2), Militia 4, Highland and Scam 3 to 5, IT 8 to 12 (9.9), British 8 to 12 (10.1) |
| generation alone (`--body-report`, mean a style; worst) | 300 to 630 ms; 5.6 s | 300 to 500 ms; 5.1 s (a Bandit seed of many plans) |
| coplanar pairs inside lofted bays (the new in-part check) | - | 311 before the ridge and cap fixes, 0 after |
| diversity, mean pair over a style's 5 shapes, old metric (Bandit, IT, Highland, Scam, British) | 0.223, 0.210, 0.231, 0.224, 0.207 | 0.219, 0.205, 0.228, 0.220, 0.208 |
| diversity, new metric (with the front term) | 0.208, 0.206, 0.225, 0.219, 0.205 | 0.206, 0.199, 0.221, 0.219, 0.204 |
| the front term alone, mean pair within a shape | 0.18 to 0.25 | 0.19 to 0.27 |
| identity, mean (Bandit, IT, Highland, Scam, British), new score | 0.93, 0.94, 0.95, 0.93, 0.96 | 0.96, 0.97, 0.96, 0.96, 0.98 |
| identity's section part alone | 0.23, 0.43, 0.65, 0.29, 0.69 | 1.00, 1.00, 1.00, 1.00, 0.92 |
| identity, old score (`--no-sections`) | 0.96, 0.96, 0.96, 0.96, 0.98 | 0.96, 0.96, 0.96, 0.96, 0.98 |

What the numbers say: lofted sections make the styles differ from each other from the front (between-style distance 20 to 78 times the fixed bays'), and a
vehicle's section now counts for its faction (a fixed-bay room reads 0.23 to 0.69 for its style, a lofted one 0.92 to 1). They don't make seeds of one shape and
one style differ more: within a shape the front term is about the same in both kits (a style keeps one language a vehicle, and the whole-body front is cab,
wheels and rails), so the diversity total moved by at most 0.01, down, the front term being smaller than the parts term it took weight from. The variety within a
style is phase 3's (proportions, a curved roofline). Two languages read alike to the profile: IT's van and Bandit's slab (a slab body on an IT bus still scores
0.95 for section; a pod on a Bandit bus 0, a cabin on anything but Scam 0).

**Committed seeds** (Styles.cs, the most different passing seeds of a 10-seed kit-5 sweep, seed 1 always among them): Bandit Box Truck 1 8 2, War Bus 1 10 6,
Flatbed Fort 1 6 7, Gun Wagon 1 4 8, Scrap Tanker 1 10 5; IT 1 2 9, 1 10 7, 1 6 3, 1 9 8, 1 2 10; Highland 1 3 7, 1 7 6, 1 8 9, 1 6 2, 1 7 4; Scam 1 6 9, 1 4 6,
1 6 2, 1 2 5, 1 6 2; British 1 9 8, 1 10 4, 1 6 2, 1 4 9, 1 8 2 (same order of shapes). 75 files replaced 75; `--gen-vehicles` reports all 123 identical.

**Foundations for the later archetypes** (muscle cars, supercars, European trucks, monster trucks, sci-fi buggies, prototypes): a language, a style's rule and the
front profile are data and general; what limits them today, and what each needs: a section is a walk-in room's (`BodyLook` keeps the half width at least 1.0 m below 1.6 m;
`HullLoft.Section.Parse` caps it at 1.30 m and the height at 3.0 m, where the 9-bit key would go to 5.11 m), bays stand on the Bolt Deck grid by feet at x = 0.75 m and are 1 m
long (1.5 m the door bay; the key allows 0.5 to 2 m), the door is fixed at x = 1.0 m and 2 m tall, and `BaySpots` uses one turret's size. A car's cabin or a
buggy's cage needs its own module (a cab or hood loft, phase 4), a scale class needs those limits per class, and a profile along the length (a hog, a wedge) needs
bay lengths and heights from the language rather than the plan's T, L and N.

## 7. Phase 2, as built (2026-11-07): wider proportions and lines

The brief: lift phase 1's fixed limits into data, give each style and seed wider proportions, draw rooflines along the length, paint Bandit bodies by rows and
sections, measure it, and make the committed vehicles again. Code: `Source/VehicleKit/VehicleBodyLanguage.cs` (all of the data below, `BodyLook`, `BaySpots`,
`PaintRuns`), `Source/Constructs/HullLoft.cs` (the part), `Vehicle.Hulls.cs` (the hull, doors), `VehicleGrammar.cs` (the plan's hooks), `VehicleChassis.cs` (ride bands).

### Scale classes (`BodyScale`)

The limits that were constants are a row per class: whether it is walked in, the half width it may reach, its height range, its bay lengths, its floor's half width,
its overhang range and the ride height bands it leans to.

| Class | Walk-in | Half width | Height | Bays | Overhang | Ride (low, mid, high) | Used by |
| --- | --- | --- | --- | --- | --- | --- | --- |
| compact | no | 0.3 to 1.0 m | 0.5 to 1.9 m | 0.5 to 1 m | 0 to 0.1 m | any | nothing yet: engine covers, cargo boxes, a car's cabin (section 8) |
| standard | yes | 1.0 to 1.28 m | 2.02 to 2.75 m | 0.75 to 1.25 m | 0 to 0.2 m | any | every style, most of Scam's and IT's |
| heavy | yes | 1.0 to 1.45 m | 2.2 to 3.05 m | 0.75 to 1.5 m | 0.1 to 0.38 m | 0.7, 1.5, 0.25 | British first, Bandit, Militia, Highland |
| giant | yes | 1.0 to 1.9 m | 2.4 to 3.8 m | 1 to 2 m | 0.2 to 0.7 m | 0.3, 1, 0 | nothing yet: its chassis is a later phase's |

A walk-in class keeps what the room check needs whatever else changes: x at least 1.0 m below 1.6 m (the furniture's metre, `Vehicle.BayHalf`) and 2.02 m at the
centre; a door bay at least 2.14 m. A non-room language (`BodyLanguage.WalkIn = false`) or class drops both. `HullLoft.Section` now reads up to 2 m of half width
and 4 m of height (`MaxCm`, `MaxHeightCm`; phase 1 refused past 1.3 and 3 m) and a floor edge from 0.3 m. A bay's feet come from its floor's width
(`HullLoft.FootX`: 0.75 m for every walk-in floor edge from 0.76 to 1.25 m, as before; further in or out for other bodies), so the part stays self-describing.

### Proportions per style (`BodyStyleRule`)

Each style's row now also holds: its classes and their weights, its height against the language (`TallMin`/`TallMax`), its share of the class's overhang
(`FlareShare`), how hard it draws its rooflines (`LineAmount`), the brow and tail lengths it rolls (a 1.25 or 1.5 m brow is a wedge nose on the room), the chance
its door is on the left (`DoorLeft`) or in the back (`DoorRear`), its ride bands (`Ride`) and its paint runs (`RunMin`, `RunMax`, `Contrast`).

| Style | Classes | Height x | Overhang share | Ride (low, mid, high) | Door left / rear | Paint runs |
| --- | --- | --- | --- | --- | --- | --- |
| Bandit | standard 2, heavy 1.5 | 0.92 to 1.25 | 1 | 0.8, 1, 1.4 | 0.5 / 0.25 | 1 to 2 bays, every other run in "body" (primer, bare metal, old paint) |
| Militia | standard 1, heavy 1 | 0.9 to 1.1 | 0.9 | 1.5, 1, 0.5 | 0.5 / 0.35 | 2 to 3 |
| IT | standard 2, heavy 1 | 0.92 to 1.16 | 0.8 | 1.6, 1, 0.3 | 0.3 / 0.35 | 2 to 3 |
| Scam | standard 2.5, heavy 0.7 | 0.95 to 1.2 | 0.5 | 1, 1, 1 | 0.5 / 0.2 | 2 to 3 |
| Highland | standard 2, heavy 1 | 0.9 to 1.14 | 1 | 1, 1, 1 | 0.5 / 0.2 | 2 to 3 |
| British | heavy 2, standard 1 | 1.0 to 1.28 | 0.7 | 1, 1.5, 0.3 | 0.2 / 0 | 2 to 3 |

**The overhang** is the section moved out above `BodyLook.FlareFrom` (0.6 m over the floor by default, raised by the hull to clear the real wheels plus 0.12 m of
travel: `Vehicle.WheelBand`, noted as "overhang from" in the kit line when it does), eased in over the language's `FlareBlend` (0.05 m a welded ledge on slab,
ledge, cabin and armour; 0.35 to 0.6 m a soft flare on pod, bus, coach and boat). The floor stays as it was, so the wheels, the deck and the furniture don't move.

**Ride height bands**: a gear recipe is low (a floor under 0.5 m between arms), mid, or high (its rear on struts that hang the wheel 0.6 m and more:
`VehicleChassis.RideOf`); the chassis layer's recipe roll weighs each by the style's band times the class's. Length stays the plan's (the recipe's ranges): a room's
bays share its length differently (`BodyLook.Lengths`: the brow and tail as rolled, the metre bays giving a quarter here and there, a long room sometimes a
quarter-metre rhythm), and a 3.5 m room keeps metre ends (its end walls are its loot's and magazine's).

### Rooflines along the length (`Roofline`)

A line is the height and the width above the furniture as factors of the language's section at each station, front (u 0) to back (u 1), lofted bay to bay:
`h = 1 + a (crown sin(pi u) + rise (2u - 1) + kick max(0, (u - 0.6) / 0.4))`, `w = 1 - a taper (2u - 1)^2`, and how hard the plan's tall, low and narrow steps
stay (`Steps`). The door bay's two stations take the higher of the two, so it is level (a turret's roof, the door's frame).

| Line | Crown | Rise | Kick | Taper | Steps | Drawn by (weight) |
| --- | --- | --- | --- | --- | --- | --- |
| plain | | | | | 1 | every language |
| stepped | | | | | 1.6 | slab 2, ledge 1.5, cabin 1 |
| crowned (hog's back) | 0.16 | | | | 0.35 | pod 2, van 1, bus 1, coach 1 |
| sagged (sway back) | -0.12 | | | | 0.5 | boat 1, slab 0.5 |
| raked (rising to the tail) | | 0.11 | | | 0.6 | coach 1.5, slab 1, van 1, armour 1 |
| sloped (down to the tail) | | -0.11 | | | 0.6 | armour 1.5, ledge 1, pod 1, cabin 0.5 |
| kick-up | | | 0.2 | | 0.7 | slab 1, ledge 1, bus 0.5, armour 0.5 |
| tapered (in plan to both ends) | 0.06 | | | 0.2 | 0.5 | pod 2, coach 1, boat 1, van 0.5, bus 0.5 |
| sheer (a boat's) | -0.08 | -0.08 | | 0.1 | 0.4 | boat 2 |

`a` is the style's `LineAmount` times a roll of 0.6 to 1. A long brow or tail never comes lower than keeps 1.68 m of room where the room check's rays start (0.35 m
in from the front, 0.3 m from the back): a 1.5 m wedge on an armour body put its roof under them (Militia War Bus 2).

### Doors

The door bay stands on either side: a left door is the right-hand door bay turned round (its key's sections swapped, stood by its back right foot), with the hinge
and leaf turned with it, the seats on the other side and the magazine's crates on the door's. A room that is the vehicle's last, 4.5 m or longer, with a bare tail
or a hitch behind it, may have its door in its back instead (the look's roll; the plan then leans 2.5 times to a bare tail or a hitch): the door bay becomes a
plain level 1.5 m bay and the tail is drawn full height with a doorway in its cap (`HullLoft` key flag `b`: jambs at 0.625 m, a 2 m opening, the cap's strips either
side and the lintel above), the hinge and leaf turned a quarter round, the back seats 0.75 m forward. Rear doors are rare (1 in 30 IT vehicles over six seeds):
most rooms that are last carry the fuel drum on a porch, which would stand in the doorway; a front door, where a cab merges with the room, waits for phase 4's cabs.

### Turret spots sized from the parts (`BaySpots.Turret`)

The turntable's half width and the arm's reach (the kit's Turntable and Square Beam 1 m, a quarter over each end) come from the part files. A roof turret needs a
level bay at least as long as the table and no bay within its reach (by distance along the room, not by index: bays are 0.75 to 1.5 m now) higher than its roof.
A side gun and a pannier now need nothing of the bay's side out past the waist over their own height (a sponson's back and a pannier's stand in the skin, so a
flared wall above or below meets them face to face: IT Box Truck 3, IT War Bus 1), as well as the clear space above that phase 1 checked.

### Paint by rows and runs

Rows: a lofted bay's painted wall rows are toned by height (below 0.95 m darker, above 1.75 m lighter: `HullLoft.Row`), so any paint keeps the section's rows;
the lowest row stays rust and the flat rows roof grey, as before. Runs: a room's bays are painted in runs (`Vehicle.PaintRuns`: a run ends where the roofline
steps, where a mixing style's language changes, or after its rolled length), one colour each, no run within 40 of a channel of its neighbour's; the wear rusts a
run, not a panel, and gives way to the contrast when it would match. Bandit runs are one or two bays and every other one, mostly, in the "body" role (primer,
bare metal, old paint), so a Bandit room reads as cobbled sections from a distance instead of one brown. The other styles' wear is light (rust 0.03 to 0.12) and
their runs keep their panel colours apart; British buses read as red and cream blocks.

### Found on the way

- **A bay's place rounded both ways.** A part's place is kept to the centimetre (`ConstructWorld.Quantise`) and a deck's sockets are on the quarter metre, so a foot
  12.5 cm in put a bay's origin on a half centimetre, which rounds to even: after metre bays always the same way, after a 0.75 m bay the other, so neighbours met
  in each other ("the previous bay is in the way"). The feet are now 12 cm from each end (a protocol change: both ends must make the same part), and a room's floor
  is laid on a half-metre lattice where the feet stand (`Deck(..., aligned: true)`; a quarter-metre plate's own centre is half a centimetre off the grid too).
- A door bay's frame (at 1.0 m) left a slot at its ends wherever the section stood out past it (a flare, a strake, a boat's 1.13 m waist even in phase 1): its ends
  are now closed from the frame's outer face out to the section (`HullLoft.DoorEnds`).
- The door frame's hidden slabs stopped a centimetre short of the bay's ends, so a neighbour's wall slab in line with them is never "in the way".
- Scam War Bus 6, made by the phase-2 kit, passed `--gen-vehicle`'s check but was refused at load ("Office Phone ... something is in the way": the loader builds a
  blueprint at the origin, the check 2 m up); it was swapped for seed 2. Not chased: a trophy that touches a part to within float error may fit at one place and
  not another, which a dynamic spawn could also meet.

### Measured (2-core box; times noisy: the "before" runs shared the box with builds)

Before is the merge base (b30b0ff8, phase 1); after is this branch. Diversity and identity: `--diversity-style STYLE --seeds 6`, the mean over the five recipe
shapes. Full check: `--sweep-vehicle --seeds 6` before, `--seeds 10` after (seeds 1 to 6 counted for the same 180).

| | before | after |
| --- | --- | --- |
| full check, 6 styles x 5 shapes x 6 seeds | 178 of 180 | 178 of 180 (the same two: Bandit Scrap Tanker 3's barrel, British War Bus 6's Firebreather 24 degrees off) |
| full check, 10 seeds | - | 295 of 300 (also Bandit War Bus 7 and British War Bus 7, a Firebreather off its gunner's look; British Scrap Tanker 10, the barrel) |
| the 75 committed kit-5 seeds, full check | 75 of 75 | 75 of 75 |
| parts a vehicle, mean over the sweeps | 129 | 126 |
| generation, sweep's mean of shape means | 5570 ms (loaded) | 1198 ms |
| generation alone (`--body-report`), mean a style; worst | 544 to 3049 ms; 45.8 s (Bandit) | 307 to 761 ms; 7.3 s (Bandit) |
| room sections from the front, mean pair within a style | 0.009 (Militia) to 0.027 (Scam) | 0.026 (British) to 0.048 (Highland) |
| ... between two styles | 0.020 to 0.078 | 0.039 (IT against British) to 0.093 (Scam against Militia) |
| coplanar pairs inside lofted bays | 0 | 0 |

| Style | diversity, mean pair within a shape | silhouette part | front part | identity |
| --- | --- | --- | --- | --- |
| Bandit | 0.206 to 0.214 | 0.116 to 0.117 | 0.188 to 0.222 | 0.962 to 0.960 |
| IT | 0.199 to 0.213 | 0.127 to 0.125 | 0.219 to 0.254 | 0.968 to 0.962 |
| Highland | 0.221 to 0.223 | 0.122 to 0.128 | 0.241 to 0.269 | 0.954 to 0.944 |
| Scam | 0.220 to 0.221 | 0.127 to 0.128 | 0.271 to 0.269 | 0.968 to 0.966 |
| British | 0.204 to 0.212 | 0.134 to 0.137 | 0.237 to 0.274 | 0.976 to 0.970 |
| Militia | 0.193 to 0.185 | 0.114 to 0.109 | 0.232 to 0.231 | 0.906 to 0.898 |

What the numbers say: within a style, rooms now differ from each other from the front two to three times as much as in phase 1 (the section term), and the front
part of the distance rises for every style but Scam and Militia. The whole distance rises little (+0.001 to +0.014; Militia -0.008, inside six seeds' noise): the
silhouette part compares each outline in its own box and is mostly the plan's (which segments, where the cab and the axles are), and phase 2 left the plan's
lengths alone; parts (0.15) and layout (0.20) don't see a body's proportions at all. Identity holds (at most -0.01; the section part compares each bay with its
style's languages as the style now draws them, overhang and taper included: `BodyLook.References`). The next rise has to come from what the distance weighs: the
plan's proportions (room length against the cab, overhangs fore and aft), phase 4's cabs and noses in the body's language, and phase 5's contour pieces.

Spread over the six seeds of every shape (`--body-report all --seeds 6`, rooms only): room heights 2.14 to 3.14 m (Bandit 2.14 to 2.79, British 2.47 to 3.14),
widest half widths 1.07 to 1.45 m (phase 1: 1.0 to 1.13), heavy class 11 of 18 Bandit, British and Militia rooms, every line of each language seen, doors left 6
to 12 and right 10 to 12 a style.

**Committed seeds** (Styles.cs, again: the most different passing seeds of a 10-seed sweep, seed 1 always among them; same order of shapes as section 6):
Bandit Box Truck 1 4 2, War Bus 1 6 10, Flatbed Fort 1 7 9, Gun Wagon 1 4 8, Scrap Tanker 1 10 5; IT 1 5 10, 1 5 2, 1 6 2, 1 9 5, 1 2 10; Scam 1 6 9, 1 4 2, 1 9 6,
1 6 2, 1 6 2; Highland 1 7 8, 1 7 6, 1 8 9, 1 6 2, 1 7 4; British 1 9 8, 1 10 8, 1 5 2, 1 4 9, 1 8 2. 75 files written (62 kept their seed, 13 new, 13 removed);
`--gen-vehicles` reports all 123 identical.

**Commands** (any vehicle tool): `--roofline NAME` (every kit-5 body with that line), `--scale NAME` (every kit-5 body at that class), with phase 1's `--language`;
`--body-report` now also prints each style's room heights and widths and its classes, lines, doors and bay-length runs.

**Not done**: a front door (phase 4's merged cabs), non-room lofted bodies (the compact class has no module yet), the giant class's chassis, room length from the
style (the recipes' ranges are the plan's), rear doors on a porch (its drum stands in the doorway).

## 8. Adding an archetype as data

A new body (a muscle car's fastback, a European cab-over box, a sci-fi gun buggy's tub, a monster truck's shell) is rows, not code, as long as it is a lofted
body on a chassis:

1. **A language** (`BodyLanguage`, VehicleBodyLanguage.cs): a name, a line of words, its tall section as a half profile (floor edge first, up and over to the
   centreline, metres; at most 16 points), its height range, its brow and tail rake and narrowing, how low and narrow its "L" and "N" sections go; then its
   `Lines` (which rooflines, how often), its `FlareBlend` (a welded ledge or a soft flare), and `WalkIn = false` if nobody walks in it.
2. **A scale class** (`BodyScale`) if none fits: its half width, height, bay length and overhang ranges, its floor's half width, whether it is walked in, and
   the ride bands it leans to. A car's body is `compact`; a land train's `giant`.
3. **A roofline** (`Roofline`) if none of the nine draws it: a fastback is a strong negative `Rise` with a short tail, a wedge a positive one with a long brow.
4. **The style's row** (`BodyStyleRule.ByStyle`): its languages and weights, its facets (smooth or faceted), whether it mixes, its classes, height factor,
   overhang share, line amount, brow and tail lengths, door sides, ride bands and paint runs.
5. Measure: `--body-report STYLE` (sections, classes, lines, doors), `--diversity-style STYLE` (the front and section terms see the new profile), `--sweep-vehicle`
   for the full check, `--vehicle-shots` with `--language`, `--roofline` and `--scale` to look at one.

What is still code, and which phase owns it: a body that is not a room (an engine cover, a cargo box, a car's cabin) needs a module that places it (phase 4's cab
and hood lofts); a front door or a cab inside the body needs phase 4; a chassis for the giant class and for a car's low, wide stance needs the chassis layer's
recipes (data in Styles.cs's gear lists, Karl's).

## 9. Phase 3, as built (2026-11-07): cabs and hoods

The brief: replace the one fixed cab part and the fixed hood parts with cabs and hoods lofted in the body's language; new cab kinds (crew, cab-over capsule, open
cab in a roll cage, armoured, merged into the body); glass; cabs and hoods in the identity and diversity measures; the committed vehicles made again. The user's
note during the work: "the enclosed sections still look very similar between factions ... I want factions to have more distinct visual looks": so each
faction's cab is its own as far as the data can make it.

Code: `Source/Constructs/HullLoft.Cabs.cs` (the parts), `Source/VehicleKit/Vehicle.Cabs.cs` (`CabLook`, `LoftCab`, `MergeCab`, the hood), the rule's new rows in
`VehicleBodyLanguage.cs` (`BodyStyleRule.Cabs`, `Noses` and the proportions below), `VehicleGrammar.cs` (the cab roll and the two new modules), `VehicleChecks.View`,
`FrontProfile.OfCab`, `VehicleIdentity.SectionScore` and `LoftedAs`. Protocol 205.

### The parts

- **A cab** (`Hull Loft (cab<kind> <11 numbers> <section> <back section>)`): the language's tall section (with the style's facets, at most 8 a side) at a cab's size,
  drawn along its length. Levels are put into the section where its rows are cut (the doorway's sill 0.15 m, the belt, an armoured cab's slit, the window
  band's top), at the same places along the back section, so a row is one row at every station. Below the belt the front is the dash at the front's plane;
  above it the windscreen rakes back toward the roof (its pillars 8 cm wide, the header solid); the sides are cut at the doorways (one a side, two on a crew
  cab) and at a capsule's mid station; a conventional, crew or armoured cab has a back wall with a window between jambs. Kinds: `e` conventional, `w` crew,
  `g` cage (no skin above the belt: hoops at the screen, the doorway and the back, shoulder rails inside the hoops, a brace across the back hoop, a bar across
  each doorway at the belt; bar families differ in size so no two share a face's plane), `a` armoured (plated, a 12 cm slit at the seated eye, 1.24 to
  1.36 m), `o` capsule (a flat raked front 2.15 m ahead of the seats, over the engine, no hood; its sides ahead of the doorway open), `m` merged (a capsule
  whose back section is its room's brow's front section, placed after the room where the brow stands: one mass, a bus front). The cab stands on the rails by
  the fixed shells' feet; hidden slabs stand inside the skin from 0.3 m up, never in a doorway, over the seats only above their backs, ahead of a capsule's
  mid station not at all (the front wheels turn there, as under the Cab Over Shell), and over the gearbox only above 0.8 m (it stands through the firewall).
- **A hood** (`Hull Loft (hood<nose> <7 numbers> <section>)`): the language's section at a hood's width and crown, lofted from the cowl to the station where the
  engine's details end (0.62 m ahead of the engine) and on to the nose section (`f` flat, `w` wedge, `r` rounded, `g` a grille box with slats proud of the cap).
  It keeps the fixed hoods' sockets (`engine` with its pad, `front` just past the cap's slab, `+x0`/`-x0` over the front wheel, `top`), so noses, rams and
  fenders go on as before. Its section clears the engine's core and details (a V8's rocker covers 12 cm over its top, 0.343 m out) by the skin and 4 cm;
  rows under 5 cm are dropped (a squashed eave mitred into a hairpin, coplanar ends). Its length comes from the plan's hood part: a Long or Bull Hood long
  (0.88 to 1.0 m ahead of the engine), a Snub Hood or Slab Nose short (0.72 to 0.8 m; a Slab Nose wide), plus the style's `HoodReach`.
- **A pane** (`Hull Loft (pane <12 numbers>)`): a see-through quad cut for one cab (its corners in the cab's frame), material glass, a seal along its bottom edge
  (the solid box it stands and collides by; a shaped box is drawn only), on the cab's `glass0` (windscreen) or `glass1` (back window) socket, turned as the
  cab is (`Vehicle.AsIs`). A shot breaks the pane, which shatters as glass does; the cab stays.

All three are run-time parts named by their keys, like a lofted bay: blueprints, saves and the wire carry only the name, and salvage takes them off as their
exact parts (a cab and a hood are welder parts, a pane a wrench part). `--fixed-cabs` makes kit-5 bodies with phase 2's fixed shells and hoods.

### Kinds and proportions per faction (data, `BodyStyleRule`)

| Style | Cab kinds (weight) | Noses | Cab height, half width | Belt | Screen rake (capsule) | Hood half width, crown |
| --- | --- | --- | --- | --- | --- | --- |
| Bandit | caged 2.5, enclosed 1, armoured 1, crew 0.7, cab-over 0.4 | grille 2, flat 1.5, wedge 1 | 1.56-1.7, 0.86-0.9 | 1.04-1.12 (high) | 0.02-0.1, upright | 0.56-0.62, 0.16-0.2 |
| Militia | armoured 2.5, crew 1.2, merged 1, caged 0.4 | wedge 2.5, flat 1 | 1.55-1.65, 0.9-0.94 | 1.08-1.12 | 0.18-0.24 (0.65-0.8) | 0.58-0.62, 0.16-0.19 |
| IT | enclosed 1.5, cab-over 1.2, crew 0.9, merged 0.8 | rounded 2, wedge 1.5 | 1.66-1.8 | 0.92-0.97 (a glasshouse) | 0.2-0.24 (0.6-0.78) | 0.5-0.55 |
| Scam | enclosed 2, crew 1.2, cab-over 0.8 | flat 2, grille 1 | 1.8-1.95, 0.92-0.95 (a tall box) | 1.0-1.04 | 0.03-0.08 (0.3-0.42) | crown 0.2-0.24 |
| Highland | enclosed 2, crew 1, caged 0.5 | grille 2.5, rounded 1 | 1.62-1.74, 0.86-0.9 | default | 0.08-0.16 | 0.48-0.54, 0.22-0.26, reach +0.1 m (a long bonnet) |
| British | merged 2, cab-over 1.2, enclosed 0.8, crew 0.5 | rounded 2, grille 1 | 1.72-1.86 | 0.94-1.0 | 0.1-0.18 (0.2-0.38, an upright bus front) | default |

A kind the plan can't take drops out of the roll: a crew cab needs a metre of bare rails behind the cab (the second row on a sill of its own at z 0.575, the
back wall at 0.95), a merged cab a room first. A Bandit cab may come off another language than its room's (the style mixes). `--cab KIND` forces a kind where
the plan allows it.

### The driver's view

`VehicleChecks.View` (in every check of a vehicle with a lofted cab): from the seated eye, 14 rays ahead (30 degrees either way, level and 4 degrees up)
through what the kit lofted (cab, hood, rooms), glass left out; straight ahead must be clear and at most a third may meet the vehicle within 4 m. A capsule's
long screen first failed it (6 of 14 met the solid side ahead of the doorway): its side windows ahead of the doorway are open now. Typical: 12 or 13 of 14 clear.

### Measured (2-core box, another agent's builds alongside: times are noisy)

Before is this branch with `--fixed-cabs` (phase 2's cabs and hoods on the same plans); after is this branch. `--body-report all --seeds 6`, `--diversity-style
STYLE --seeds 6` (mean over the five recipe shapes) and `--identity STYLE --seeds 4`.

| | before | after |
| --- | --- | --- |
| cabs and hoods, front and side outline: mean pair within a style | 0.067 | 0.055 |
| ... between two styles | 0.069 (the same as within: a cab didn't tell the faction) | 0.083 |
| ... between over within | 1.03 | 1.52 (1.64 in a first run with IT's capsules weighed higher) |
| full check, 6 styles x 5 recipe shapes x 6 seeds | - | 177 of 180 (Bandit Scrap Tanker 3's barrel and British War Bus 6's Firebreather, as phase 2; Highland War Bus 5 new: fills 61 % of its side box, a capsule in front of a tall room) |
| the 75 committed kit-5 seeds, full check | 75 of 75 | 75 of 75 |
| coplanar pairs inside lofted parts (bays, cabs, hoods, panes) | 0 | 0 (292 in the first run: the cage's hoops, a pane's seal, squashed hood eaves; then the cab's foot row against the decks, fixed) |
| parts a vehicle, mean (`--body-report`) | 121 to 129 | 122 to 130 (a lofted cab is one part as a shell was, plus 1 or 2 panes) |
| generation, mean a style (`--body-report`) | 275 to 519 ms | 263 to 524 ms |

| Style | diversity, mean pair within a shape | front part | identity | best other style's score | section part of identity |
| --- | --- | --- | --- | --- | --- |
| Bandit | 0.211 to 0.209 | 0.202 to 0.202 | 0.96 to 0.95 | 0.55 to 0.54 | 0.98 to 0.97 |
| IT | 0.211 to 0.201 | 0.241 to 0.230 | 0.95 to 0.92 | 0.53 to 0.51 | 0.91 to 0.88 |
| Highland | 0.219 to 0.205 | 0.247 to 0.204 | 0.95 to 0.95 | 0.59 to 0.58 | 0.88 to 0.96 |
| Scam | 0.217 to 0.216 | 0.244 to 0.248 | 0.95 to 0.93 | 0.52 to 0.51 | 0.99 to 0.99 |
| British | 0.209 to 0.203 | 0.256 to 0.270 | 0.96 to 0.94 | 0.53 to 0.52 | 0.88 to 0.91 |
| Militia | 0.183 to 0.173 | 0.220 to 0.181 | 0.89 to 0.90 | 0.78 to 0.78 | 0.77 to 0.88 |

What the numbers say: the cab now tells the faction (between-style cab distance up a fifth, within-style down a fifth, so the ratio from 1.03 to 1.52), the
point of the user's note. That came at the cost the brief hoped to avoid: diversity within a style fell a little (-0.002 to -0.014), because the fixed shells a
style drew from (a Tin Roof Cab, a Chop Top, a Roll Bar...) differed more from each other than one faction's lofted cabs do, and each faction's proportions are
now narrow ranges of its own. Identity holds within 0.03; the margin over the best other style holds or rises. The section part rises for Highland, British and
Militia (their cabs and hoods speak their languages) and falls for IT (its capsules' clamped lower sides read less like a pod). Militia's best other style
stays at 0.78 (it read as Bandit before this phase too). To raise variety within a faction without losing it between factions: two or three cab sub-styles a
faction (Bandit: a cage, a chopped enclosed cab, a slab-armoured cab, each with its own proportions), side windows and quarter glass, capsule side glass.

**Committed seeds** (Styles.cs, the most different passing seeds of a 10-seed sweep, seed 1 always among them; same order of shapes as section 6): Bandit
Box Truck 1 4 2, War Bus 1 6 10, Flatbed Fort 1 7 9, Gun Wagon 1 8 2, Scrap Tanker 1 10 5; IT 1 5 3, 1 5 10, 1 2 7, 1 9 7, 1 2 10; Scam 1 6 9, 1 4 6, 1 9 6,
1 6 5, 1 6 2; Highland 1 3 7, 1 7 6, 1 8 9, 1 6 2, 1 2 7; British 1 9 8, 1 3 5, 1 5 7, 1 4 5, 1 8 2. 75 files written: 62 rewritten, 13 new, 13 removed
(BanditGunWagon4, BritishFlatbedFort2, BritishGunWagon9, BritishWarBus8, BritishWarBus10, HighlandBoxTruck8, HighlandScrapTanker4, ITBoxTruck10,
ITFlatbedFort6, ITGunWagon5, ITWarBus2, ScamGunWagon2, ScamWarBus2); `--gen-vehicles` reports all 123 identical. The kit-1 shapes (Technical, Tanker Rig,
Fuel Trailer, Double Decker) keep their generators and fixed cabs: they have no body look to loft from (moving them needs a look rolled for them and their own
cab station: a later phase's).

**Not done / for the next phase.** Side glass (a capsule's and a merged cab's side windows are open, a crew cab's rear quarters solid); a merged cab's room
keeps its brow's rake, so the cab sits a step below a tall room (a merged room could roll a full-height brow); doors that open (the doorways are open frames);
a crew cab on any plan (it needs the bare-rails gap); interiors (dash, steering wheel). For the coming faction dressing and surface phase, what showed here:
the factions now differ in their cabs' silhouettes, but rooms still read alike in a single colour: Bandit bodies want welded plates and cage bars on the rooms
too (the cage cab shows how bars read), IT smooth fairings and dish domes on capsules, Scam signboards over the tall boxy cab, Highland chrome grille and long
bonnet ornaments, British two-tone bus livery and a destination board over a merged front, Militia stowage bins and slat armour; capsules need glass sides to
read as vehicles from three quarters.

## 10. Phase 4, as built (2026-11-08): faction exterior identity

The brief: rooms that read as their faction even in one colour (surface treatment, trim, belts and strakes, fenders and skirts, the unused contour parts,
proportions), phase 3's suggestions (Bandit welded plates and cage bars; IT fairings and dish domes; Scam signboards; Highland chrome grilles and bonnet
ornaments; British two-tone livery and destination boards; Militia stowage bins and slat armour, and Militia no longer reading as Bandit), glass in capsules'
sides, a merged cab's room without the step, sub-styles within a faction, measured by cross-faction separation without losing variety within a faction.

Code: `Source/Constructs/HullLoft.Trim.cs` (the parts), `Source/VehicleKit/Vehicle.Trim.cs` (placing them), the style rows' `Trims`, `TrimMix`, `TrimShare` and
`Decos` (`BodyStyleRule`, VehicleBodyLanguage.cs), `FrontProfile.Exterior` and the new `--body-report` matrices, `VehicleIdentity` (trim and fittings as traits,
Militia's own signature), Militia's slots (Styles.cs). Protocol 206.

### The parts

- **A trim** (`Hull Loft (trim<kind><variant> <length, lowest> <section> <section>)`): cut for one side of one lofted bay from the bay's own two sections (the -x side
  takes them back to front), so it follows the bay's taper, flare and roofline. It stands on the bay's waist socket (`+x0`, `-x0`), so a pannier, a sponson or a
  mast that took a waist first keeps it, and it is placed last, after the guns and the dressing. Its pieces are flat panels up the wall, each a chord between two
  places on the section stood off so its inner face clears the skin's outer face by 11 mm (a part's place rounds to the centimetre: 6 mm and more whatever the
  rounding), split every 25 cm with a 6 mm seam; a piece over a ledge or a flare (the wall bending away from its chord by more than 3.5 cm) is left out. Its
  lowest is the wheels' room on that side (a tyre's top and 12 cm of travel), its highest where either section turns into its roof. Kinds:

  | Kind | Faction | What it is |
  | --- | --- | --- |
  | `p` plates | Bandit | two or three welded plates on the lower, middle and upper wall, each off another wreck (bare metal or old paint) |
  | `c` cage | Bandit | bars over the upper wall on two rails, stood off on brackets (the cage cab's bars on the room) |
  | `s` slats | Militia | slat armour: a slat every 11 cm from the wheels to 1.45 m, 14 cm off the skin on three posts |
  | `b` bins | Militia | one or two stowage bins on the lower wall, lidded and strapped |
  | `f` fairing | IT | a smooth skirt fairing over the lower wall, a link-blue stripe over the waist, louvred vents at the back now and then |
  | `g` signboards | Scam | a signboard on the upper wall in the war paint (WINNER yellow, hot pink, dollar green), a dark kick strip low down |
  | `r` clinker | Highland | varnished planks up the lower hull, each a step further out than the one below, and a wooden strake along the waist (and under the eaves on tall rooms) |
  | `l` livery | British | a waistband in the second colour (cream, racing green or navy over bus red), a chrome rubbing strip over it and one under the windows |

- **A fitting** (`Hull Loft (deco<kind> <numbers>)`): `S` a roof sign on two posts (Scam's cab), `D` a lit destination board over a merged or capsule front
  (British), `R` a radome on a ring (IT's cab), `O` a chrome bonnet mascot near the nose (Highland), `G` a chrome grille of frame and slats on a lofted hood's
  nose where the plan's nose is a grille or none (Highland), `B` a roof basket (Militia's cab), `J` a jerry can (Militia's trophy slot: a fixed key,
  `HullLoft.JerryCan`). Sized from the cab and hood they go on (`CabShape`, `HoodShape`).

Both are run-time parts named by their keys, as a lofted bay is: blueprints, saves and the wire carry the name; they are welder parts (salvaged as themselves);
what they draw is detail, and the solid box they stand by is a small one at the socket's plane, at a height where the bay's own hidden slabs (a row's step at
its corners' mean, a centimetre inside the skin) stay clear of it with half a centimetre to spare (`StandSpot`), with a second at the socket's height out past
the slabs when that height isn't clear (a socket must lie within its part's solid bounds). Each goes on with `clear: true` (no face on another part's plane)
and a check that it draws into nothing on the vehicle but the rooms' own bays and what stands inside them (`DrawnClash`: every box's bounds against every
other part's), and moves on when refused. `--no-trim` makes kit-5 bodies without them (to measure against).

### Per faction (data, `BodyStyleRule`)

| Style | Trim sets (weight) | Mix | Share of free sides | Fittings (chance) |
| --- | --- | --- | --- | --- |
| Bandit | plates 2, plates and cage 2, cage 1 | each side its own of the set | 0.7 | - (the cage cab is its own) |
| Militia | slats 2, bins 1.5, slats and bins 1.5 (in turns down the room) | no | 0.9 | roof basket 0.7; jerry cans as its trophies, nothing on its sides |
| IT | fairing | no | 1 | radome 0.75 |
| Scam | signboards | no | 0.8 | roof sign 0.9 |
| Highland | clinker | no | 1 | mascot 0.8, chrome grille 0.75 |
| British | livery | no | 1 | destination board 0.9 (merged and capsule fronts) |

The sets are the sub-styles within a faction (Bandit and Militia three each); the trims' own rolls (a plate's place and size, bins one or two, vents, a sign's
height) vary the rest. Militia's dressing was the Bandit's (iron spikes, a hubcap plate), so it read as Bandit: it now hangs jerry cans where bandits hang
trophies and nothing down its sides, with a signature of its own (`VehicleIdentity`: jerry cans as its mark; slats, bins, the basket, slit cabs as traits).
Every style's traits count its trim and fittings (`LoftedAs`: "trim plates", "deco radome").

**Capsule glass.** A capsule's and a merged cab's side windows ahead of their doorways (open since phase 3) are glazed: a pane each side on the opening's chord
between the belt and the window band's top, behind the pillar (`glass2`, `glass3`; a pane's seal runs along its bottom edge whichever way that goes). **Merged
fronts.** A merged cab's room keeps its brow's front at 0.86 of the room's height or more (`BodyLook.MergedFront`), so the cab no longer sits a step below a tall
room; its width stays as rolled (a full-width brow made plans read as boxes from above).

### Measured (2-core box; before is main at 030d3d97 with the new measure only)

`--body-report all --seeds 6` (every recipe shape of six styles), `--diversity-style STYLE --seeds 6` (the mean over the five recipe shapes), `--identity STYLE
--seeds 4`, and the full check `--sweep-vehicle "STYLE SHAPE" --seeds 6` over six styles and five shapes.

The new measure (`FrontProfile.Exterior`): **room exteriors** compares two vehicles' rooms with everything on their outside, as the mean of the front profile
(trim, panniers and sponsons in it) and the **relief**: in twelve bands of the room's height, the share of its length where something stands a centimetre or
more proud of the skin, sampled over every drawn triangle (so it sees belts, slats, boards and planks whatever their colour). **Whole body** compares the body's
front profile and its side outline (the top's height in twelve slices along it).

| | before | after |
| --- | --- | --- |
| room exteriors: mean pair within a style | 0.043 | 0.106 |
| ... between two styles | 0.053 | 0.205 |
| ... between over within | 1.22 | 1.93 |
| ... nearest other style over the style's own (Bandit, IT, Highland, Scam, British, Militia) | 0.89, 0.87, 0.83, 1.24, 1.24, 1.13 (three styles nearer another than themselves) | 1.39, 1.61, 1.43, 1.87, 1.87, 1.19 |
| whole body (front and side outline), between over within | 1.03 | 1.04 |
| cabs and hoods (phase 3's measure), between over within | 1.52 | 1.55 |
| room sections from the front, within / between | 0.025 to 0.050 / 0.039 to 0.093 | the same |
| trims and fittings a vehicle (Bandit, IT, Highland, Scam, British, Militia) | - | 2.8 and 0, 4.2 and 0.6, 4.1 and 1.1, 3.6 and 0.9, 4.3 and 0.5, 3.5 and 0.2 |
| full check, 6 styles x 5 shapes x 6 seeds | 177 of 180 | 177 of 180 (the same three: Bandit Scrap Tanker 3's barrel, British War Bus 6's Firebreather 25 degrees off, Highland War Bus 5 a box at 61 %) |
| coplanar pairs inside lofted parts (bays, cabs, hoods, panes, trims, fittings) | 0 | 0 (152, then 372 on the way: see below) |
| parts a vehicle, mean (`--body-report`) | 122 to 130 | 128 to 136 |
| generation, mean a style (`--body-report`); worst | 285 to 1330 ms; 26.8 s (a Bandit seed of many plans) | 286 to 738 ms; 5.8 s |
| full check sweeps, parts a vehicle and generation, mean of the 30 | - | 133 parts, 1382 ms |

| Style | diversity, mean pair within a shape | front part | identity | best other style's score, mean (worst) |
| --- | --- | --- | --- | --- |
| Bandit | 0.209 to 0.211 | 0.202 to 0.200 | 0.95 to 0.95 | 0.54 (0.75) to 0.35 (0.63) |
| IT | 0.201 to 0.202 | 0.230 to 0.236 | 0.92 to 0.92 | 0.51 (0.63) to 0.51 (0.63) |
| Highland | 0.205 to 0.203 | 0.204 to 0.204 | 0.95 to 0.96 | 0.58 (0.76) to 0.59 (0.76) |
| Scam | 0.216 to 0.214 | 0.248 to 0.247 | 0.93 to 0.94 | 0.51 (0.63) to 0.51 (0.63) |
| British | 0.203 to 0.210 | 0.270 to 0.282 | 0.94 to 0.94 | 0.52 (0.63) to 0.52 (0.63) |
| Militia | 0.173 to 0.169 | 0.181 to 0.151 | 0.90 to 0.87 | 0.78 (0.83) to 0.59 (0.65) |

What the numbers say: a room now tells its faction by what is on it, not only by its section: between-style distance of the room exteriors almost four times
what it was, every style nearer itself than any other (three were nearer British before), and within a style the rooms differ more too (0.043 to 0.106: trim
sets, plates and bins where the dice put them). Militia no longer reads as Bandit (best other 0.78 to 0.59, and Bandit's own best other, which was Militia,
0.54 to 0.35); its own identity fell a little (0.90 to 0.87) because its signature is now its own and asks for jerry cans and its trim rather than taking
whatever it carried. Diversity within a style holds (-0.004 to +0.007, inside six seeds' noise; Militia's front part fell, every Militia body now being as wide
with its slats). The whole body from the front and the side barely moves (1.03 to 1.04): the trim is centimetres on a silhouette of metres, and the outline is
the plan's; that is the next phase's (proportions and new spaces by faction). Identity of IT, Scam and British didn't rise: their trait scores were already at
their cap.

**Found on the way.**
- A part's place is kept to the centimetre (the 2026-11-07 lesson again): a trim cut to stand 6 mm off the skin from its socket's exact place sat as close as
  0 mm and as far as 11 when the socket's place rounded, and its solid box met the bay's slabs it was cut to miss. The gap is now 11 mm and the box keeps 6 mm
  off the slabs. A first try that followed the section facet by facet with mitres overlapped itself at the joints of thick pieces (bins, posts) and over
  ledges; flat pieces with 6 mm seams, every other one 3 mm shorter, replaced it.
- A trim on a bay's -x side reads the bay's sections back to front, and a row's hidden slabs are counted in steps along the bay's front section, so where the two
  sections' rows differ in length the trim's own count of the steps was another: `StandSpot` takes both counts.
- Two coplanar faces in phase-3 parts that no sweep had met (plans moved with the new traits): a windscreen pane whose top leans in shared a face plane with its
  seal's end (seals are now 1.5 cm short of the glass's sides), and a cage cab with a pitched roof had its two ridge bars meet face to face (their sizes differ
  now). Both change the parts their keys name, so protocol 206.
- Glazing a capsule's sides fills the outline the silhouette rule measures: with every cab forced to `merged`, two of eight plans that passed before read as a
  box (and plan again in a normal run, to another cab).

**Committed seeds**: the same seeds as phase 3 but IT Scrap Tanker 10, which now fails as Bandit Scrap Tanker 3 does (no socket takes the barrel: its plan
moved), replaced by seed 5 (the sweep's most different with 1 and 2). 66 files written again, 1 removed, 1 new; `--gen-vehicles` reports all 123 identical.

**Not done**: the seven unused contour parts (Armour Slope, Slant Sheet, Roof Slant, Skirt Sheet, Tube Bend) are still unused (trim made at run time does
their job on rooms; wiring them to beds, hoods and skirts needs their sockets turned for each place); fenders and arches per faction; cab sub-styles beyond
the trim sets; the whole-body outline per faction; Highland War Bus 5 (still a box at 61 %, with or without trim: it is the plan, not the dressing); fittings
on cabs of kit-1 shapes (the Technical, the Double Decker); a network round trip of a trim between two peers (it is a run-time part by its key exactly as a
lofted bay is, which phase 1 proved over the network; not sent here).

## 11. Phase 5, as built (2026-11-09): new spaces

The brief: new kinds of room per faction (raised and double-deck rooms with a stair, side pods, caged or open-frame rooms), the Double Decker made by the
grammar, walkable upper floors with a path up to them, the room walk and hole-ray checks passing on every new space, the driver's view kept, no coplanar faces,
separation not worse than phase 4 (and the whole body's raised), diversity within a style not lower, and the full check at 177 of 180 or better.

Code: `Source/Constructs/HullLoft.Spaces.cs` (the parts), `HullLoft.cs` (a bay key's fourth word), `Source/VehicleKit/Vehicle.Spaces.cs` (the storey and its stair,
pods), `VehicleGrammar.cs` (`Spaces`, `PlanUpper`, `PlaceStair`), the style rows' `Cage`, `CageKind`, `Uppers`, `Upper*`, `Bulwark*`, `Pods`, `PodMax`
(`BodyStyleRule`, VehicleBodyLanguage.cs), `VehicleChecks` (`Upper`, `Climb`, `Contained`), a recipe's own storey (`ShapeRecipe.Upper`, `UpperShare`: the Double Decker). Protocol 217.

### The parts

- **A caged bay** (a lofted bay's key with a fourth word `c` and a pattern): the bay's two sections drawn as a frame of bars, seen and shot through. Rails along
  the bay at every point of the section (the floor rail 3 cm up, off the floor's plane), more between them at the pattern's spacing, bows across inside each end
  and in the middle, and the faction's infill: `w` welded diagonals (Bandit), `s` green slats below 1.6 m and a mesh above (Militia), `t` timber stakes
  (Highland). Its hidden boxes are the bars' own, so a player is held in by the rails and a round passes between them. A door bay keeps a flat frame round its
  doorway. Caged rooms take no roof rack.
- **An upper floor** (`u` and the hatch's rectangle): a bay's roof cut level, its slab walked on, with sockets on the quarter grid for the storey's feet and its
  furniture, cut round the hatch a stair comes up through. **An open top** (`o`): a bay with no roof row, the bulwark round an open deck.
- **A stair** (`Hull Loft (stair <rise> <steps> <width>)`): treads up from the room's floor to the hatch, a ship ladder's rise (a player steps up 0.55 m).
- **A side pod** (`Hull Loft (pod...)`): a compartment on a middle bay's waist socket, cut for the bay's own sections as a trim is, in the faction's profile:
  `w` a welded box with a sloped lid (Bandit, at chance 0: it washed out the trim), `b` a smooth blister (IT), `k` a kiosk with a window and a sign band (Scam, 0),
  `l` a clinker locker (Highland, 0), `a` an armoured stowage pod (Militia), `g` a luggage locker (British).

### The plan

A room is caged at the style's chance, else a long room may take an upper storey over a run of its middle bays (never the brow or the tail, which rake down):
an enclosed **saloon** lofted in the body's language at the storey's height and width (a set-in pod storey on IT, flush with the walls on a bus), or an open
**deck** behind a bulwark. The run's roofs are cut level (a door bay's 2.16 m at least) and are its floor. The stair stands against a side wall inside the run,
on the floor's quarter grid, clear of the gunners' seats, the doorway, the magazine at the front and a back door, with a hatch a player fits through and room to
step off at the top; a plan with no wall for it drops the storey. The way onto the stair is kept clear of furniture (`Room.Keep`). Rates by style:

| Style | cage | upper storey | pods |
| --- | --- | --- | --- |
| Bandit | welded, 0.5 | none | (welded, 0) |
| IT | - | saloon 1 in 3, set in to 0.84 of the wall | blisters, 0.5 a free bay side, 2 at most |
| Highland | timber, 0.06 | deck 1 in 3 | (lockers, 0) |
| Scam | - | saloon 1 in 2 | (kiosks, 0) |
| British | - | saloon 10 in 11, flush with the walls, over 90 to 100 % of the middle (a double deck) | (luggage lockers, not used) |
| Militia | slatted, 0.1 | deck 1 in 3.5 | armoured stowage, 0.5, 2 at most |

**The Double Decker** is the grammar's now (`ShapeRecipe.Upper` "deck" over 45 to 75 % of the room's middle, an open top deck behind its bulwark, reached by the stair; a deck over all of it read as a box from the side): kit 1's generator makes it only with `--kit 1`.

### The checks

- A caged room and an open deck **hold a player in**: a standing body moved out from the middle to either side and either end meets a bar or the bulwark before
  it is past the walls (`Contained`; not the walker, which steps onto any top from beside it). Bars are seen and shot through by design, so a caged room has no
  hole-ray check; an enclosed saloon has the rays as any room.
- The **storey**: a player stands on its floor away from the hatch, has the headroom under an enclosed one, walks it, and **walks up the stair** from the room's
  floor onto it (`Climb`: a body along the stair's line, feet on the highest top a step or less up, held by anything it meets; the failure names what held it).
- The room below: a player standing in its middle may stand on the stair, so the check looks along the middle, then along the aisle a body's width off the
  middle away from the stair, for a spot on the floor.
- The driver's view (14 rays out of the cab) and the coplanar check (between parts, and `--body-report`'s inside every lofted part) cover the new parts.

### Measured (2-core box; before is main at 05a79a55)

`--body-report all --seeds 6`, `--diversity-style STYLE --seeds 6` (the mean of every shape's mean pair), and the full check `--sweep-vehicle "STYLE SHAPE"
--seeds 6` over six styles and five shapes.

| | before | after |
| --- | --- | --- |
| room exteriors (front with trim, and relief), between over within | 1.94 (0.205 over 0.106) | 1.48 (0.195 over 0.132) |
| ... the same with `--no-spaces` | - | 1.95 |
| ... nearest other style over the style's own (Bandit, IT, Highland, Scam, British, Militia) | 1.38, 1.60, 1.45, 1.80, 1.85, 1.19 | 1.34, 1.36, 1.05, 1.34, 1.31, 1.06 |
| whole body (front and side outline), between over within | 1.04 | 1.03 |
| cabs and hoods, between over within | 1.56 | 1.58 |
| room sections from the front, mean pair within a style (Bandit .. Militia) | 0.041, 0.044, 0.050, 0.038, 0.025, 0.031 | 0.040, 0.071, 0.052, 0.056, 0.037, 0.035 |
| diversity within a style, mean pair (Bandit .. Militia) | 0.207, 0.200, 0.206, 0.206, 0.204, 0.172 | 0.209, 0.198, 0.210, 0.209, 0.212, 0.175 |
| full check, 6 styles x 5 shapes x 6 seeds | 177 of 180 | 177 of 180 (the same three: Bandit Scrap Tanker 3's barrel, Highland War Bus 5 a box at 61 %, Highland Scrap Tanker 3's Pipe Box) |
| British Double Decker, seeds 1 to 10 | kit 1 | 7 of 10 (2: nowhere for a gun; 5 and 6 read as a box at 70 and 64 %) |
| coplanar pairs inside lofted parts | 0 | 0 |
| parts a vehicle, mean; generation, mean a style | 132 to 142; 305 to 1487 ms | 131 to 141; 352 to 791 ms |

What the numbers say: the new spaces give each style rooms that differ more among themselves (the room sections' within-style spread rises most on IT, Scam and
British, which take storeys), and that is what lowers the room exteriors' separation: with `--no-spaces` it is 1.95 as before, cages alone cost about 0.2 of it,
storeys about 0.2 and pods about 0.1 (`--no-cages`, `--no-uppers`, `--no-pods` on the way). A space is a roll, so within a style a caged or two-storey room
stands beside skinned ones; Highland and Militia now come nearest each other (decks and cages on both). The whole body's outline didn't rise (1.04 to 1.03):
storeys are rolled within every style that takes them, so they widen the spread inside a style as much as between styles. Raising either needs the rates
to become signatures (most of a style's long rooms caged, or two-storey), a design choice left to the user. Diversity within a style holds or rises
(IT -0.002, inside six seeds' noise).

**Found on the way** (this agent's part, finishing the phase):
- A Bandit welded cage had two diagonals of neighbouring wall rows meet at the corner nearly in one plane (a ledge's wall, 0.05 mm apart): each now stops
  1.8 cm short of its row's ends, inside the rail there.
- Scam War Bus 2's stair was blocked by a Water Cooler: furniture was kept off the stair's way by its planned spot, and `Put` moved it up to 0.2 m onto a
  socket inside the way. The keep test now takes that reach.
- Highland War Bus 4 failed "a player stands 0.96 m off its floor": the stair stands by the middle, and both ends of the middle had gunners' seats; the stand
  search now also tries the aisle beside the stair.

**Committed seeds**: the Double Decker's are 1, 4, 9 (the sweep's most different passing seeds; 5 and 13 removed). 16 other files written again (vehicles whose
plans took a space or whose trim moved with the plan's new dice); `--gen-vehicles` reports all 123 identical.

**Not done**: rates as faction signatures (above); kiosk, locker and welded-box pods are made but at chance 0; a storey over a short room; a caged cab and a caged
room meeting without a skinned bay between them is not checked for bars meeting face to face beyond the coplanar check; a network round trip of the new parts
between two peers (they are run-time parts by their keys exactly as lofted bays are, which phase 1 proved over the network; not sent here); the British Double
Decker's three failing seeds.

## 12. Phase 6, as built (2026-11-09): balconies and lopsided vehicles

The brief (the user's words): "I'd like to see balconies, and more asymmetry on the vehicle sometimes." Balconies reached from inside, styled per faction, passing
the room checks; asymmetry by style as data, the vehicle level and driving; the room exteriors' separation (1.94 before phase 5, 1.48 after it) raised where the
new rates can be faction signatures.

Code: `Source/Constructs/HullLoft.Balcony.cs` (the part), `Source/VehicleKit/Vehicle.Balcony.cs` (placing it; `Lean`), `Vehicle.Hulls.cs` (the door's ladder moves to
the balcony), `Vehicle.Spaces.cs`, `Vehicle.Trim.cs` and `Vehicle.Body.cs` (pods, trim and spikes by `Lean`), the style rows' `Balcony*` and `Asym*`
(`BodyStyleRule`, VehicleBodyLanguage.cs), `VehicleChecks.Balcony`. Protocol 219.

### The part

**A balcony** (`Hull Loft (balc<kind> <length, depth, rail, inset, rise> <open end>)`): a deck at the room's floor (its top 4 mm under it) outside the door, posts
round its outer edge and one end at most 0.7 m apart, a top and a middle rail (their hidden boxes hold a player in), an edge beam and two knee brackets under it,
and the faction's infill. The other end is open: the door's ladder hangs there (on the balcony's `ladder` socket) instead of under the doorway. It stands on the
room floor's free edge socket outward nearest the doorway (`base`), so it is an ordinary socketed part (no freeform weld): the key carries how far its deck's
inner edge stands past that socket (a centimetre past whatever stands out of the wall there: jambs, the hinge, the floor's edge) and how far its deck's top is
over it. Kinds: `w` welded grating with zigzag diagonals (Bandit), `s` green slats outside the posts (Militia), `f` an ivory fairing panel under a chrome rail
(IT), `g` chrome posts with gold knobs and a lit sign band (Scam), `t` timber posts, rails and balusters on planks (Highland), `r` thin dark wrought balusters
(British). Every family of pieces differs in size from those it meets (posts 5 cm, rails 4.5 and 4, infill 3 and less), so no two share a face's plane.

A side door's balcony keeps to its own bay, a middle one (its length at most the bay's): past it a cab's side, a brow's rake or a neighbour's pods and sponsons
come later and would stand over its deck. It is refused (and the ladder goes under the door as before) when more than 0.3 m stands out of the wall at the deck's
height (a flare, a wheel), when it would draw into anything, and when a flared wall over it would meet a standing player's head and a deck up to 1.2 m deep
wouldn't clear it. A door in the tail's back takes one across the back (1.3 to 1.7 m); no style rolls a rear door often enough to show it in six seeds.

### Lopsided vehicles

`BodyStyleRule.Asym` is the chance a vehicle is lopsided (`Lean`, its own fork of the seed: toward its side balcony when it has one). A lopsided vehicle hangs
its pods on that side only (one more than `PodMax`, at `AsymPod` a free bay side at least: Bandit's welded boxes, Scam's kiosks and Highland's lockers, at 0
otherwise, show only here), thins the far side's trim by `AsymTrim` (Bandit only: thinning another faction's trim washed out its sides' signature), and with
`AsymAmount` 0.5 or more puts its spikes on the one side. Pods weigh 20 to 120 kg on a 2.5 to 3.5 t vehicle: every lopsided seed stood level and passed the drive
check, so no balancing was needed. An offset cab and uneven roof clutter were left out (the cab's place feeds the drive line and the view check).

| Style | balcony (kind, chance a door) | lopsided (chance, amount) | pods when lopsided | other rates changed (phase 5's) |
| --- | --- | --- | --- | --- |
| Bandit | welded, 0.35 | 0.7, 0.9 (trim thinned 0.9) | welded boxes 0.6 | cage 0.5 to 0.6 |
| Militia | (slatted, 0) | 0.35, 0.6 | stowage 0.6 | cage 0.1 to 0.05, deck 1 to 0.4 |
| IT | (faired, 0) | 0.1, 0.4 | - | - |
| Scam | showy, 1 (0.95 to 1.1 m deep) | 0.4, 0.7 | kiosks 0.7 | - |
| Highland | timber, 1 (0.95 to 1.15 m deep) | 0.3, 0.5 | lockers 0.5 | cage 0.06 to 0, deck 1 to 0.4 |
| British | wrought, 1 (rail 1.0 to 1.1 m) | 0.05, 0.3 | - | - |

### The check

`VehicleChecks.Balcony`, with the room's: a player stands on the deck's middle (a few ticks: once the walker rides a deck it walks forward), walks out from
inside the doorway onto it (with the door open), and moving outward or toward the closed end from its middle meets the rail. The door's own check, the room's
hole rays, the driver's view and the coplanar checks are unchanged and cover it.

### Measured (2-core box shared with other agents' builds: times are noisy; before is main at 8af91803)

`--body-report all --seeds 6`, `--diversity-style STYLE --seeds 6` (mean of the five recipe shapes' mean pair), the full check `--sweep-vehicle "STYLE SHAPE"
--seeds 6` over six styles and five shapes.

| | before | after |
| --- | --- | --- |
| room exteriors, between over within | 1.48 (0.195 over 0.132) | 1.48 (0.190 over 0.128); 1.37 with the first rates (balconies and lopsidedness rolled per vehicle in every style) |
| ... nearest other style over the style's own (Bandit, IT, Highland, Scam, British, Militia) | 1.34, 1.36, 1.05, 1.34, 1.31, 1.06 | 1.51, 1.33, 1.42, 1.26, 1.25, 0.99 |
| whole body (front and side outline), between over within | 1.03 | 1.02 |
| cabs and hoods, between over within | 1.58 | 1.59 |
| diversity within a style, mean pair (Bandit .. Militia) | 0.221, 0.204, 0.222, 0.228, 0.218, 0.192 | 0.221, 0.204, 0.213, 0.223, 0.218, 0.191 |
| identity, mean | 0.94, 0.93, 0.95, 0.95, 0.94, 0.89 | the same |
| balconies (vehicles with rooms: Bandit, Highland, Scam, British) | - | 2 of 18, 10 of 19, 12 of 22, 7 of 21 |
| lopsided (vehicles with rooms, Bandit .. Militia) | - | 14 of 18, 3 of 18, 6 of 19, 10 of 22, 2 of 21, 6 of 18 |
| full check, 6 styles x 5 shapes x 6 seeds | 177 of 180 | 177 of 180 (the same three: Bandit Scrap Tanker 3's barrel, Highland War Bus 5 a box, Highland Scrap Tanker 3's Pipe Box) |
| coplanar pairs inside lofted parts | 0 | 0 |
| generation, mean a style (`--body-report`); worst | 398 to 1076 ms; 11.5 s | 417 to 2094 ms; 48.5 s (a Bandit seed of many plans: more caged rooms re-plan more) |

What the numbers say: as rolled extras in every style (the first rates), balconies and lopsidedness widened each style's own spread more than they parted the
styles (1.48 to 1.37). As signatures (a balcony at every door it fits on Scam, Highland and British, none on IT and Militia; Bandit lopsided most of the time and
caged more often; Highland's decks and stock wagons and Militia's cages and decks rarer, since those two read as each other) the room exteriors' separation is
back to 1.48, with Highland now well apart (1.05 to 1.42) and Bandit further (1.34 to 1.51); Militia is the weakest (0.99, nearest Highland: without its rarer
cages and decks it has slats and bins only, which phase 4 measured close to Highland's laps). The phase-4 level (1.94) wasn't reached: spaces are still rolled per
room. Diversity within a style holds but for Highland (-0.009: its decks and cages made rarer) and Scam (-0.005). The whole body's outline doesn't move:
balconies stand inside the side outline's silhouette. Balconies fit on half of the doors at most: flared walls, wheels under the doorway and front or tail door
bays refuse them.

**Found on the way.**
- A freeform-welded balcony isn't on segment 0, so `Put` (which takes only segment-0 parts) never found its ladder socket and hung the ladder on the nearest
  socket of anything, under the chassis: the drive check moved 0.00 m. The balcony now stands on the floor's edge socket.
- The walker steps a player who rides a deck forward each tick (`ConstructRide.Step` with Forward held), so a 40-tick stand walked off an open end; and a
  0.92 m player met a middle post on a 0.8 m deck and was pushed under it. The stand test takes 14 ticks, decks are 0.92 m deep at least, and a failure names
  what stands in the player's way.

**Committed seeds**: unchanged; 41 files written again (balconies, lopsided pods and the retuned rates moved their plans); `--gen-vehicles` reports all 123
identical; `--check-construct-parts` clean.

**Not done**: balconies off an upper storey (a saloon's end or side would need a door bay in the storey, and an open deck's bulwark a gap); balconies at a front
or tail door bay; an offset cab and uneven roof clutter; Militia's separation (0.99: its own new space, a slatted firing step, is data at 0 because it read as
Highland's veranda); Bandit's worst generation (48 s with cages at 0.6); a network round trip of a balcony between two peers (a run-time part by its key exactly
as a pod is; not sent here).

## 13. Phase 7a, as built (2026-11-09): interiors that follow the body

The brief (the user's decisions): the inside look per faction, walls and ceiling either following the hull's own shape or an inset lining, chosen per faction as
data, nothing through the hull and no coplanar faces with it; furnishing by role (ammo racks for gunships, bunks for carriers, racks for haulers), faction flavour
props, loot stowed where a player would, working stations; always a walkable aisle; density by faction as data; lamps per faction and windows where the body has
glass. This phase is ground-floor rooms and cabs; upper storeys, pods, caged rooms and balconies are 7b's.

Code: `Source/Constructs/HullLoft.Lining.cs` (the lining and lamps, drawn by the bay), `HullLoft.cs` (a bay key's last word), `HullLoft.Cabs.cs` (`Dash`, a cab
key's last word), `Vehicle.Rooms.cs` (`Interior`, `Furnish`), `Vehicle.Cabs.cs` (`PlaceCab`), the style rows' `Lining`, `LiningInset`, `Clutter`, `Props`, `Fill`,
`Gear` (`BodyStyleRule`, VehicleBodyLanguage.cs), `BodyLook.Lining`, `LiningInset` and `Names`. Protocol unchanged (219): a lining is a word on a run-time part's
key, which the wire carries as text.

### The lining

A ground-floor bay's key takes a last word `i`, the kind and the mode (`Hull Loft (100 ~... ~... irf)`); the bay draws it, so a room has no more parts than
before. Every piece stands in from the skin's inner face by 4 mm or more and is drawn only: a player stands against the bay's hidden slabs (1 to 5 cm inside the
skin), which a following lining (2.4 to 4 cm inside) never reaches past. Pieces end short of each other at a corner (by the offset's overlap there), are at most
0.5 m along a row (planks 0.15 m) with 6 mm seams, every other one 3 mm shorter along the bay, and stop 1.5 cm short of an open bay end (2.6 cm at a closed one, past
the cap). Modes:

- `f` **follows** the hull's rows: sloped walls, tumblehomes, curved and pitched roofs, ledges (a row under 8 cm is left bare).
- `i` an **inset** lining: upright walls where the hull is narrowest up to 2.05 m and a flat ceiling as high as the walls stay upright and the roof clears it. A bay
  where that would stand more than 12 cm off the hull, or put the ceiling under 2.05 m, follows instead. The lever socket hangs under an inset ceiling.

A door bay's doorway side is bare below the cant; an upper floor's ceiling isn't lined (its hatch); closed ends (brow and tail caps) aren't lined yet.

| Style | kind | mode (chance inset) | lamp | dash |
| --- | --- | --- | --- | --- |
| Bandit | `r` bare hull: ribs across the section (one a bay, two over 0.9 m) and weld beads along the wall rows, broken round the ribs | follows | a bare bulb on a flex | rusty, amber gauges |
| Militia | `m` olive panels, a dark rack rail at 1.1 m | 0.8 | a caged bulkhead lamp | olive, green |
| IT | `c` pale panels, a blue-grey kick strip | 0.9 | a flush light panel | pale, blue |
| Scam | `s` purple below a gold rail at 1.05 m, dark above, pink neon along an inset wall's top | 0.75 | a pink neon tube | purple, pink |
| Highland | `t` varnished planks, two tones | 0.1 | a lantern on a hook | timber, amber |
| British | `u` padded moquette under a wood dado at 1.0 m, cream panelling above, cream ceiling | 0.2 | a chrome-rimmed dome lamp | wood, cream |

Lamps are glowing boxes (drawn bright and unlit, as a lit lamp's lens is), one a bay of 0.7 m and more, 0.32 m off the centreline (the lever hangs on it), only
where the roof is 2 m up or more. They light nothing around them: construct lamps don't cast light (dynamic lights are capped at four a frame,
docs/render/LIGHTING.md). **Windows**: rooms have no glass to keep clear (a room's windows are 7b's); cabs keep theirs, the dash stands under the belt.

**Cabs**: an enclosed, crew, armoured or caged cab's key takes `i` and the kind (when the key has room for it): a dash shelf under the belt across the front, 6 mm in
from its inner face, 5 cm clear of the sides, over the gearbox (0.82 m up and more), with a glowing gauge strip. A capsule's and a merged cab's front is over the
engine, far from the seats: no dash yet.

### Furnishing

`Furnish` on a kit-5 room (`Interior`) puts, down the side walls by its old rules (never in the doorway, the middle, a stair's way; the aisle kept): **role gear**
first, `Gear` pieces of it (Ammo Containers where a room's seats work guns or for a raider, gun platform, boss or command; bunks for a carrier or a room 5 m and longer
with no gunners; a Parts Shelf, a Wheel Rack or a shelf otherwise); then the layout, its repeats in a long room scaled by `Fill`; then the faction's props,
`Clutter` a metre of room. Containers (Ammo Containers, shelves, racks) roll loot and take the cargo pass's stowage as any furniture does. A named piece is put no
further than 0.1 m from where it was asked, 0.17 m from what stands on that wall already (where it stands, not where it was asked) and 0.9 m from the room's ends
(an end chest): two backs on one wall line meet in a plane wherever they overlap (Scam War Bus 4 and Gun Wagon 5 on the way). What doesn't fit is left out, so the
walls set the density as much as the rule.

| Style | gear | fill | clutter a metre | props |
| --- | --- | --- | --- | --- |
| Bandit | 2 | 1 | 0.9 | Fuel Drum, Ammo Crate, Crate Stack, Small Fuel Tank, Ammo Box |
| Militia | 2 | 0.6 | 0.25 | Ammo Crate, Small Fuel Tank, Cable Reel |
| IT | 1 | 0.35 | 0.12 | Office Plant, Network Switch, Desk Fan |
| Scam | 1 | 0.8 | 0.6 | Office Phone, Call Headset, Gift Card Sign, Loudhailer, CRT Monitor |
| Highland | 1 | 0.9 | 0.45 | Wee Keg, Thistle |
| British | 1 | 0.8 | 0.35 | Teapot, Garden Gnome, Union Flag, Traffic Cone |

`--no-interiors` makes rooms as before (no lining, dash, gear or props; the same dice), to measure against.

### Measured (2-core box; before is this branch with `--no-interiors`, which makes main's vehicles)

`--body-report all --seeds 6` and the full check `--sweep-vehicle "STYLE SHAPE" --seeds 6` over six styles and five shapes.

| | before | after |
| --- | --- | --- |
| full check, 6 styles x 5 shapes x 6 seeds | 177 of 180 | 177 of 180 (the same three: Bandit Scrap Tanker 3's barrel, Highland War Bus 5 a box, Highland Scrap Tanker 3's Pipe Box) |
| coplanar pairs inside lofted parts | 0 | 0 (65, then 10 on the way: a weld bead's outer face on a rib's, then beads and ribs meeting nearly in a plane on twisting rows: beads now stand 3 mm further in and break round the ribs) |
| room exteriors, cabs and hoods, whole body (between over within) | 1.48, 1.59, 1.02 | 1.48, 1.57, 1.02 (an interior is inside the outline; the cab measure moved with plans that furnishing sent to another attempt, within its run-to-run spread of 1.57 to 1.59) |
| parts a vehicle, mean a style (`--body-report`, Bandit .. Militia) | 130, 136, 140, 141, 138, 133 | 130, 136, 141, 141, 139, 133 (a lining and a dash are drawn by the bay and the cab: no parts; gear and props stand where the walls have room, and plans move) |
| parts, mean a shape in the full check | - | War Bus 163 to 188, Box Truck 141 to 152, Flatbed Fort 158 to 171, Gun Wagon 108 to 134, Scrap Tanker 68 to 87 |
| generation, mean a style; worst | 282 to 562 ms; 5.8 s (Bandit Box Truck 1) | 293 to 567 ms; 5.6 s (the same). Phase 6's 48.5 s worst didn't come back in either run |

**Found on the way.**
- A prop stood with its back on the same wall line as the next piece of furniture, and `Put` moved it up to 0.2 m onto a socket, so the two overlapped in a plane
  (Scam War Bus 4: an Ammo Container and a Gift Card Sign; Scam Gun Wagon 5: a Loudhailer by an end chest). Named pieces now move 0.1 m at most and keep 0.17 m from
  where the others stand.
- An Ammo Container holds rounds, not slots: placed first, it took the wall a short room's only container would have stood on, and Highland Box Truck 5 planned
  again into a top-heavy plan. Racks without slots go after the last-resort chest; an end chest keeps clear of the gear.

**Committed seeds**: unchanged; 66 files written again (lined bays, dashes, gear and props; 189 lined bays in all); `--gen-vehicles` reports all 123 identical;
`--check-construct-parts` clean.

**Left for 7b.** Upper storeys' and side pods' insides, caged rooms and balconies (no lining: bars and open tops); closed ends (brow and tail caps) and the back
door's cap; windows cut in rooms (they have no glass); a capsule's and a merged cab's dash; a steering wheel; working stations wired (a radio, a powered
workbench, a lamp on a switch: today's stations are the gunners' seats and the magazine's feeds, as before); props on shelves and workbenches rather than the floor
(Bandit's clutter is held back by wall length); lamps that light their surroundings; a network round trip of a lined bay between two peers (a run-time part by its
key as phase 1 proved; not sent here); nothing was tried in the game window.

## 14. Phase 7b, as built (2026-11-09): windows and lamps that light

The brief: phase 7a's left-overs, in order: room windows where the body has glass (per faction, glass counting as closed, no coplanar faces); lamps that really
light the room through the game's light system within the frame budget; then upper storeys', pods', cages' and balconies' insides, closed end walls' lining, a dash
in capsule and merged cabs, a steering wheel, props on shelves and wired stations. Windows and lamps were done; the rest is listed under "Not done".

Code: `Source/Constructs/HullLoft.Lining.cs` (`WindowKinds`, `WindowOf`, `RoomPanesOf`, `WindowLining`, the lamps' lights), `HullLoft.cs` (`WindowRow`, the window's
pad and the slabs cut round it), `Vehicle.Hulls.cs` (`Glaze`), the style rows' `Window` and `WindowChance` (`BodyStyleRule`), `Source/Render/ConstructLamps.cs`,
`LightGrid` (`DynamicFree`, mode 5), `Lighting.cs` (`ApplyLightAt`), `Program.Lighting.cs`. Protocol 221: a lined bay's lining word may carry a window letter
(`i` kind mode window, `Hull Loft (100 ~... = iuft)`), which a 220 peer can't make.

### Windows

A lined bay's key takes the faction's window letter on its lining word when the vehicle rolls windows (the style's `WindowChance`, on a fork of its own). The bay
cuts a window in each side wall but a door bay's doorway side, centred along it, when the bay is the window's width and 15 cm either side long, and the window's
sill and head each fall 3 cm or more inside a row, the same row on both sections, with every row between upright (half again as tall as it leans). The sill
stands 5 cm over the waist's pad at least (a pannier's or a sponson's socket; a pane's seal would meet it). The wall rows it crosses are cut in four: under the
sill and over the head the bay's length, and a jamb either side on every row between (6 mm thinner than the skin, so their faces stand 3 mm in from the pieces
over and under them: on a twisting bay they met nearly in a plane). The hidden slabs behind the wall stop either side of it. A pane (the cab's pane part, its
corners in the bay's frame) stands on a hidden pad at the sill's middle (`win-x`, `win+x`), a centimetre into the jambs and the head; its glass counts as closed
for the room's hole rays, and a shot breaks the pane, not the bay. The opening is too small for a player. The lining is cut round it 2 cm back (a reveal): a
following lining's jambs stand 6 mm further in than the pieces over and under them, an inset lining's panels split round the band; a bare hull's weld bead stops
on that row. Room panes go in the `roomglass` list, not the cab's `glass`, which the cab measure and the identity gate read.

| Style | window | chance a vehicle | width, sill to head |
| --- | --- | --- | --- |
| Bandit | `s` slit | 0.5 | 0.6 m, 1.45 to 1.62 m |
| Militia | `s` slit | 0.6 | 0.6 m, 1.45 to 1.62 m |
| IT | `b` band | 0.9 | 0.9 m, 1.22 to 1.72 m |
| Scam | `b` band | 0.85 | 0.9 m, 1.22 to 1.72 m |
| Highland | `p` port | 0.75 | 0.45 m, 1.3 to 1.72 m |
| British | `t` tall bus window | 0.95 | 0.6 m, 1.2 to 1.82 m |

### Lamps that light

Each lamp a lining hangs (phase 7a's, one a bay of 0.7 m and more under a 2 m roof) is also a light: the bay's part carries its lamps' points and colours
(`ConstructPartDef.Lamps`, made with the part from its key: no file field, nothing on the wire), and a Lamp logic part that is on is one too. Each frame,
`ConstructLamps.AddLights` gives the dynamic lights the frame has left after flashes and blasts (at most four in all, `LightGrid.MaxDynamic`) to the lamps nearest
the eye within 16 m, at the bodies' drawn pose, 4.5 m of reach, each faded as it nears the reach or the next lamp left out, so lights hand over without popping.
A body's lamp parts are gathered once a rebuild (`Construct.LampParts`, by `RebuildStamp`): nothing allocates in a steady frame. On an unlit map (every Sandbox map)
the light block's new mode 5 adds the dynamic lights to the classic light in `ApplyLightAt`, so every scene shader takes them; muzzle flashes still light lit maps
only, as before. A point light has no shadow: a lamp lights the inner faces round it, faces turned away from it get nothing (a hull's outside, a roof's top), and
the ground under the floor a little (4.5 m reach from 2 m up).

Cost: at most four lights a frame whatever the number of vehicles (the shader loop lit maps already run for flashes); CPU a pass over the bodies within 46 m and
their lamp parts. Lamps a vehicle with rooms (`--body-report`): Bandit 1.1, IT 1.9, Highland 1.5, Scam 1.5, British 2.2, Militia 1.0. The frame cost wasn't
measured on a GPU (the container has none); the shader compiled and the smoke test ran with GL error 0.

### Measured (2-core box shared with other agents: times are noisy; before is this branch with `--no-interiors`, which makes main's vehicles less 7a)

| | before | after |
| --- | --- | --- |
| full check, 6 styles x 5 shapes x 6 seeds | 177 of 180 (phase 7a) | 177 of 180 (the same three: Bandit Scrap Tanker 3, Highland War Bus 5, Highland Scrap Tanker 3); room walks, hole rays and balconies pass with glass in; parts, mean a shape: 68 (Bandit Scrap Tanker) to 190 (Highland War Bus) |
| coplanar pairs inside lofted parts | 0 | 0 (14, then 1 on the way: under-sill pieces and jambs, then the lining's, on twisting bays) |
| room exteriors, cabs and hoods, whole body (between over within) | 1.48, 1.59, 1.02 | 1.46, 1.57, 1.02 (the cabs as phase 7a; windows bring the room exteriors a little closer: within 0.127, between 0.185 from 0.190) |
| parts a vehicle, mean a style (Bandit .. Militia) | 130, 136, 140, 141, 138, 133 | 130, 136, 142, 141, 139, 134 (panes: 0.7 to 3.8 a vehicle with rooms, cab panes included) |
| generation, mean a style; worst | 282 to 527 ms; 5.3 s (Bandit Box Truck 1) | 296 to 390 ms, Bandit 1127 ms (554 ms in a Bandit-only run); worst 5.0 s in a Bandit-only run (Bandit Box Truck 1), but the same vehicle took 23.4 s in each `all` run, unexplained |

**Found on the way.**
- A socket facing up must stand on a solid face: the pane's socket stands on a hidden pad.
- The pane's seal met the waist's pad and the wall's hidden slabs on a leaning row (Scam Flatbed Fort 1, Militia Box Truck 6): the sill keeps over the waist and
  the slabs stop at the window.
- Room panes in the cab's `glass` list moved the cab measure from 1.59 to 1.19 and sent Bandit seeds to more plans; they have a list of their own.

**Committed seeds**: unchanged; 34 files written again (137 glazed bays and their panes); `--gen-vehicles` reports all 123 identical; `--check-construct-parts`
clean.

**Not done** (7c): upper storeys', side pods', caged rooms' and balconies' insides; lining on closed end walls; a dash in capsule and merged cabs; a steering
wheel; props on shelves; wired stations (a radio, a powered workbench, lamps on a switch: a Lamp logic part already lights when on); the `all` run's Bandit Box
Truck 1 time; a network round trip of a glazed bay; nothing was tried in the game window with a kit vehicle near (the lights' look is unseen).

## Pictures

Phase 4: `/home/claude/vk-body4-sheets/` (agent box, not committed): `before_` (main, 030d3d97) and `after_` `--vehicle-sheet` of every style's War Bus and Box
Truck, seeds 1 to 6.

Phase 3: `/home/claude/vk-body3-sheets/` (agent box, not committed): `before_` (main before this phase) and `after_` `--vehicle-sheet` of Bandit War Bus, IT Box
Truck and British War Bus, seeds 1 to 6.


Phase 2: `/home/claude/vk-body2-sheets/` (agent box, not committed): `before_` and `after_` `--vehicle-sheet` of Bandit War Bus, IT Box Truck and British War Bus,
seeds 1 to 6. Phase 1's in `/home/claude/vk-bodies-sheets/` (agent box; PNG):

- `sections_languages.png`: the cross-sections from the front, today's T/L/N and the nine languages, with a player's box for scale.
- `languages_BanditWarBus2.png`: one plan (Bandit War Bus 2) with its room in each language and in today's bays, rear three-quarter and side.
- `kit3_<Style>_<Shape>.png` and `kit5_<Style>_<Shape>.png`: `--vehicle-sheet` of seeds 31 to 36, Box Truck and War Bus, five styles, today against the prototype
  (the same plans: only the rooms differ).

## Measuring it again

Phase 2's numbers come from `--diversity-style STYLE --seeds 6`, `--body-report all --seeds 6` and `--sweep-vehicle "STYLE SHAPE" --seeds 10` over the six styles
and five recipe shapes, run once on the merge base and once on the branch.

```
--body-report all --seeds 4 --kit 3      # the fixed bays
--body-report all --seeds 4              # kit 5, current (and coplanar pairs inside lofted bays)
--diversity-style IT --seeds 6 --no-sections   # the old distance and identity, to compare
--sweep-vehicle "IT War Bus" --seeds 6 --kit 5
--vehicle-shots "Bandit War Bus 2" --kit 5 --language cabin   (xvfb)
--vehicle-shots "IT War Bus 2" --roofline crowned --scale heavy   (xvfb; phase 2: one line, one class)
--body-report all --seeds 6 --no-trim    # phase 4: without the faction trim and fittings (the room exteriors matrix, relief by band)
--body-report all --seeds 6 --no-spaces  # phase 5: without cages, storeys and pods (or --no-cages, --no-uppers, --no-pods)
--sweep-vehicle "British Double Decker" --seeds 10   # phase 5: the grammar's double deck
--body-report all --seeds 6 --no-balconies   # phase 6: without balconies (or --no-asym: no lopsided vehicles)
--body-report all --seeds 6 --no-interiors   # phase 7a: without linings, dashes, role gear and props (and phase 7b's windows)
```
