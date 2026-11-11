# Body styles: a proposal (vehicle kit)

History (phase records): BODY-STYLES-HISTORY.md. Read it only when you need why something was built this way.

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
