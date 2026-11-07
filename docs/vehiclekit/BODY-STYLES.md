# Body styles: a proposal (vehicle kit)

The user's request (2026-11-07): "Investigate new body styles. Currently the factions' vehicles are fairly similar in style. New pieces potentially, but more
specifically, the lines of the vehicle have very cookie-cutter pieces. All of the enclosed spaces are only one of three varieties. Considering how many vehicles we're
going to implement, I want this feature to be good on the toolkit before I move forward with creating more vehicles." Earlier reviews said the same from other sides:
"still too boxy overall, and rounding corners won't fix that", "few slopes and bends; the kit needs vehicle-appropriate contour and body pieces; make more parts if
needed", "interiors too alike", "cabs too alike: no roll cages, just a flat wall".

**Status (2026-11-07): phase 1 is built** (section 6): kit 5 is the current kit, every committed faction vehicle was made again by it, the tools see sections, and
the user's decisions are in section 5. Sections 1 to 4 are the investigation as it was written, before the build.

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

**The part** (`HullLoft`): sections up to 16 points, in a compact key (`~` and three characters a point, `=` for a back section that is the front's: protocol 197),
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

## Pictures

In `/home/claude/vk-bodies-sheets/` (agent box; PNG):

- `sections_languages.png`: the cross-sections from the front, today's T/L/N and the nine languages, with a player's box for scale.
- `languages_BanditWarBus2.png`: one plan (Bandit War Bus 2) with its room in each language and in today's bays, rear three-quarter and side.
- `kit3_<Style>_<Shape>.png` and `kit5_<Style>_<Shape>.png`: `--vehicle-sheet` of seeds 31 to 36, Box Truck and War Bus, five styles, today against the prototype
  (the same plans: only the rooms differ).

## Measuring it again

```
--body-report all --seeds 4 --kit 3      # the fixed bays
--body-report all --seeds 4              # kit 5, current (and coplanar pairs inside lofted bays)
--diversity-style IT --seeds 6 --no-sections   # the old distance and identity, to compare
--sweep-vehicle "IT War Bus" --seeds 6 --kit 5
--vehicle-shots "Bandit War Bus 2" --kit 5 --language cabin   (xvfb)
```
