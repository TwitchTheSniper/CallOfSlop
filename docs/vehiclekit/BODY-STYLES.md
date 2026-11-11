# Body styles (vehicle kit)

History (phase records): BODY-STYLES-HISTORY.md. Read it only when you need why something was built this way.

The user's request (2026-11-07): "Investigate new body styles. Currently the factions' vehicles are fairly similar in style. New pieces potentially, but more
specifically, the lines of the vehicle have very cookie-cutter pieces. All of the enclosed spaces are only one of three varieties. Considering how many vehicles we're
going to implement, I want this feature to be good on the toolkit before I move forward with creating more vehicles." Earlier reviews said the same from other sides:
"still too boxy overall, and rounding corners won't fix that", "few slopes and bends; the kit needs vehicle-appropriate contour and body pieces; make more parts if
needed", "interiors too alike", "cabs too alike: no roll cages, just a flat wall".

**Status (2026-11-11): phases 1 to 9b are built** (BODY-STYLES-HISTORY.md sections 6 to 20): kit 5 is current, with lofted rooms, cabs and hoods in each
faction's body language, faction trim and fittings, new spaces, balconies, lopsided vehicles, interiors, and an archetype library with six extra shapes. "How it
works" below is the current summary; sections 1 to 4 of the history are the investigation as it was written, before the build. Section 5 here holds the user's
answers, section 8 how a later archetype is added as data.

## How it works

The body layer of the vehicle kit as it stands (kit 5, `VehicleKitVersion.Current = Lofted` in `VehicleGrammar.cs`). Each subsection ends with the history
section it was taken from; the numbers there (rates per style, measures) are as they were when that phase was built, and the data in the code is the truth.

### Lofted parts (`HullLoft`)

A room's bays, cabs, hoods, panes, trims, fittings, pods, stairs and balconies are run-time parts made from a key in their name (`Hull Loft (...)`), not part
files: `Source/Constructs/HullLoft.cs` and its partial files (`.Cabs`, `.Trim`, `.Spaces`, `.Balcony`, `.Lining`). A bay is lofted between a front and a back
section (half profiles of up to 16 points, floor edge first); blueprints, saves and the wire carry only the name, and the part is rebuilt from it. Any panel
that is not exactly flat is split into two flat triangles (`ConvexHull.Of` drops near-duplicate faces); facets meeting at a point are mitred (`HullLoft.Mitres`).
Every walk-in section keeps the room check's needs: at least 1.0 m of half width below 1.6 m (`Vehicle.BayHalf`) and 2.02 m at the centre. Lofted parts are welder
parts (panes are wrench parts): salvage takes one off as itself, by its key, and the attach tool builds it onto anything; there is no Assembling recipe for them.
History: sections 3 and 6 ("The part", "Salvage").

### Body languages, scale classes and rooflines

A **language** (`BodyLanguage`, `Source/VehicleKit/VehicleBodyLanguage.cs`) is a named section shape with its height range, brow and tail rake, and its low
and narrow variants: the nine base languages are slab, ledge, pod, van, bus, coach, boat, cabin and armour, and the archetypes add their own
(`VehicleArchetypes.cs`). Each style's row (`BodyStyleRule.ByStyle`) weighs its languages, sets its facets (smooth styles 8 to 12 a side, faceted ones as written),
and whether it mixes languages along a vehicle (Bandit: cobbled from wrecks). The language is rolled on the body layer's own fork (`BodyLook.Roll`), so nothing else
of a seed moves. A **scale class** (`BodyScale`: compact, standard, heavy, giant) holds the size limits (half width, height, bay lengths, overhang, ride bands),
and a **roofline** (`Roofline`: plain, stepped, crowned, sagged, raked, sloped, kick-up, tapered, sheer, and the archetypes' fastback, wedge and level) bends the
height and width along the room. The style's row also sets height, overhang share, brow and tail lengths, door sides and paint runs (`PaintRuns`: a room is
painted in runs of bays, and rows toned by height). Turret, side-gun, pannier and roof-rack spots come from the bays as built (`BaySpots`), not from plan names.
History: sections 6 ("Languages per faction", "Turret and gear spots") and 7.

### Rooms: doors, spaces, balconies, lopsided vehicles

- **Doors** stand on either side, or in the back of a last room 4.5 m or longer with a bare tail or a hitch (key flag `b`). History: section 7, "Doors".
- **New spaces** (`HullLoft.Spaces.cs`, `Vehicle.Spaces.cs`, the grammar's `Spaces`, `PlanUpper`, `PlaceStair`): a caged bay (a frame of bars with the faction's
  infill: welded, slatted or timber), an upper storey over a run of middle bays (an enclosed saloon or an open deck behind a bulwark) reached by a stair through a
  hatch, and side pods on a bay's waist. The Double Decker is the grammar's (`ShapeRecipe.Upper`). The checks: `VehicleChecks` holds a player in a caged room or on
  an open deck (`Contained`) and walks up the stair (`Climb`). History: section 11.
- **Balconies** (`HullLoft.Balcony.cs`, `Vehicle.Balcony.cs`): a deck outside the side door with the faction's railing, the door's ladder hung at its open end;
  refused where the wall stands out or a flared wall would meet a standing head. Checked by `VehicleChecks.Balcony`. **Lopsided vehicles**: `BodyStyleRule.Asym`
  is the chance a vehicle leans to one side (`Lean`): its pods, thinned trim and spikes go on that side. History: section 12.

### Cabs and hoods

`Source/Constructs/HullLoft.Cabs.cs` (the parts) and `Source/VehicleKit/Vehicle.Cabs.cs` (`CabLook`, `LoftCab`, `MergeCab`, the hood). A cab is lofted in the
body's language; kinds are `e` conventional, `w` crew, `g` cage, `a` armoured, `o` capsule (cab-over) and `m` merged (its back section is its room's brow's
front, one mass like a bus front). A hood is lofted from the cowl to the nose (`f` flat, `w` wedge, `r` rounded, `g` grille) and keeps the fixed hoods'
sockets. Panes are glass parts on the cab's `glass` sockets; a shot breaks the pane, not the cab. Each faction's kinds, noses and proportions are data
(`BodyStyleRule.Cabs`, `Noses`), and each faction has two or three **cab sub-styles** (`CabStyle`, rolled on its own fork, laid over the rule after the
archetype by `Vehicle.UseArchetype`). `VehicleChecks.View` looks from the seated eye through 14 rays: straight ahead must be clear. `--cab KIND` forces a kind,
`--fixed-cabs` uses the old shells, `--no-cab-styles` leaves the sub-styles out. History: sections 9 and 19 ("Cab sub-styles").

### Faction exterior: trim and fittings

`HullLoft.Trim.cs` and `Vehicle.Trim.cs`. A **trim** is cut for one side of one lofted bay from the bay's own sections and stands on its waist socket, placed
last: Bandit plates and cage bars, Militia slats and bins, IT fairings, Scam signboards, Highland clinker planks, British livery. A **fitting** is a single
piece sized from the cab or hood: a roof sign, a destination board, a radome, a mascot, a chrome grille, a roof basket, Militia's jerry cans. Both go on with
`clear: true` and are refused when they would draw into anything (`DrawnClash`). The style row's `Trims`, `TrimMix`, `TrimShare` and `Decos` choose them; the
sets are the faction's sub-styles. `--no-trim` leaves them out. History: section 10 (and 19, "Fenders, rear doors, Militia", for Militia's current sets).

### Archetypes

An archetype (`BodyArchetype`, `VehicleArchetypes.cs`) is a row that lays its languages, cab kinds, noses, proportions, scale classes and ride bands over a
faction's own rule (`Vehicle.Rule`): the archetype changes the silhouette, the faction keeps its paint, trim, fittings, lining, props and guns. A vehicle rolls one
on its own fork ("archetype") at the shape's `BodyArchetype.Share`; the extra shapes (Car, Buggy, Monster Truck, Wagon, Concept, Giant Truck; `Generators/`,
made by grammar recipes) always take one. Families: muscle cars, a supercar, European cars, vans and trucks, gun and scrap buggies, monster trucks, war wagons and
carriages, bubble cars and streamliners, the giant hauler. A Car has a cabin, a hood, a boot lofted over the fuel tank (`TankSpot`) and a gun on a pedestal; an
archetype may name its own body plans (`BodyArchetype.Bodies`). Militia builds none. Section 8 below is how to add one. `--archetype NAME` forces one,
`--no-archetypes` leaves them out. History: sections 16 to 18 ("How it works") and 19 ("Faction car outlines").

### Interiors: linings, furnishing, windows, lamps

- **Linings** (`HullLoft.Lining.cs`): a ground-floor bay's key takes a last word `i` (kind, mode, window), and the bay draws the lining itself, so a room has no
  more parts. Modes: `f` follows the hull's rows, or an inset lining with upright walls and a flat ceiling where that stays within 12 cm of the hull. Each
  faction has its kind (Bandit's bare ribs and weld beads, Militia olive panels, IT pale panels, Scam purple and gold, Highland planks, British moquette and
  panelling). Upper saloons and open decks are lined, closed ends too (`FollowEnd`); caged rooms, pods and balconies are not. History: sections 13 ("The lining")
  and 15.
- **Furnishing** (`Vehicle.Rooms.cs`: `Interior`, `Furnish`, `OnFurniture`; `Vehicle.Spaces.cs`: `FurnishUpper`): role gear first (ammo containers, bunks,
  shelves and racks), then the layout, then the faction's props by the style's `Gear`, `Fill`, `Clutter` and `Props`; a prop with no floor stands on a piece of
  furniture. The aisle and the stair's way are kept. History: sections 13 ("Furnishing") and 15 ("Props on furniture").
- **Cabs** draw a dash (a console in capsule and merged cabs) and a steering wheel (`HullLoft.Cabs.cs`, `Dash`, `SteeringWheel`). History: section 15, "Cabs".
- **Windows**: a lined bay may cut a window in each side wall (the style's `Window` and `WindowChance`), glazed with a pane that counts as closed for the room's
  hole rays. **Lamps** are glowing boxes the lining hangs, and lights too: `ConstructLamps.AddLights` gives the frame's free dynamic lights (at most
  `LightGrid.MaxDynamic`, four) to the lamps nearest the eye. History: section 14.
- `--no-interiors` makes rooms without any of this (the same dice).

### The `--body-report` measures

`--body-report STYLE|all [--seeds N] [--from S]` (`BodyReportCli` in `VehicleBodyLanguage.cs`) generates seeds of each style and prints, per style: vehicles and
those with rooms, distinct bay parts and sections, languages, facets a side, room heights and widest half width, the classes, lines, doors and bay length runs,
the front profile's mean pair and generation time, and the interior's lamps and panes a vehicle; then the cab separation (within a style against between
styles), the room exteriors' matrix with relief by band, trims and fittings a vehicle, and at the end the failures and the **coplanar pairs inside every
lofted bay, cab, hood, pane, trim and fitting**. The `--no-...` switches (`--no-trim`,
`--no-spaces`, `--no-cages`, `--no-uppers`, `--no-pods`, `--no-balconies`, `--no-asym`, `--no-archetypes`, `--no-interiors`, `--no-cab-styles`; `--no-sections`
for the identity and diversity) make the same seeds without one layer, to measure against. The front profile (`FrontProfile`) also feeds `VehicleDiversity`'s
front term and `VehicleIdentity`'s section term. "Measuring it again" below lists the runs used. History: sections 6 ("Tools see sections"), 10 and the
"Measured" subsections.

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
--body-report all --seeds 6 --no-archetypes  # phase 8a: the styles' own bodies, without archetypes
--sweep-vehicle "IT Box Truck" --seeds 6 --archetype "rigid box"   # phase 8a: one archetype on every seed that can take it
--sweep-vehicle "Bandit Car" --seeds 8   # phase 8a part 2: the car body
--sweep-vehicle "Bandit Monster Truck" --seeds 8   # phase 8b: a new shape (Buggy, Monster Truck, Wagon, Concept, Giant Truck)
--body-report all --seeds 6 --no-interiors   # phase 7a: without linings, dashes, role gear and props (and phase 7b's windows, phase 7c's ends, storeys, consoles and wheels)
--body-report all --seeds 4 --no-cab-styles   # phase 9a: without the factions' cab sub-styles
--armoury-report --seeds 6 --check   # phase 9b: the full check over the five recipe shapes in every style
--sweep-vehicle "IT Technical" --seeds 8   # phase 9b: the Technical (and the Tanker Rig) on the grammar
```
