# The Roamer (the rust bucket's camper sister)

Karl (2026-11-20): "another Rust Bucket style ship? Glass, sort of like a winnebago styling similar to the panels it has."
Generator: `Source/PartKit/Generators/RoamerParts.cs` (`--gen-parts roamer_parts`), which writes `Constructs/Parts/Roamer/*.json`
and `Constructs/Blueprints/Roamer.json`. Change the generator, never its output. Shown on the Ship gallery (`#roamer`).

- **Built like the rust bucket** (ship.md): the same lofted hull of five points a side through seven rings, split into the same
  welded pieces (Keel, Deck, Roof, hull runs, frames, partitions, Chin, Canopy, Tail), and the same rooms and sockets inside a room
  box, so every fitting hangs where the rust bucket's does. It reuses the rust bucket's drives, lift jets, legs, radiators and mast;
  its own parts are the structure, the ramp, the pods and saddle tanks (narrower hull), the Air Unit and the Roof Rack.
- **The hull**: a motorhome's box with rounded corners (the sides near vertical amidships, the corners chamfered), a full-width
  nose with a wraparound windscreen raked back under a cab-over brow (a small dark window, amber clearance lamps), a rounded rear
  cap with a rear window, a ladder to the roof and tall tail lamps.
- **Glass**: the windscreen runs round into the cheeks; the cab has a glass roof; the cockpit's side windows are big. Down both
  sides a band of framed windows (`Windows`) looks into the rooms: each is a hole in the skin with glass, a rubber gasket and a
  chrome sill, a reveal back to the room's own wall, glass in that wall, a wooden valance and orange curtains inside. The bathroom's
  is frosted. Skylights on the roof.
- **Colours**: faded cream paint; the camper's brown, orange and mustard stripes along the sides, across the nose and sweeping up
  round the rear corners; chrome trim (bumper, drip rails, sill bars, mirrors); the rust bucket's patch panels below the deck line.
- **The rear** (Karl, 2026-11-20: "the rear contour space needs to be taken up a little more and sealed", then "utilise the space
  ... expanding the rooms, hall etc."): the rear cap sits about 1 m closer in, and the rooms run aft into it (`Back` 16.8, past
  ring 5 at 16.06), their side walls running in a straight taper from ring 5 to the back wall (`TaperIn`), no steps and no filler behind it (Karl: "remove that weird cavity filler at the back, connect the walls").
- **The stair** (Karl, 2026-11-20, from a picture: an open steel stair, "we want to be able to shove stuff under the stairs"): the
  Roamer Stair (`OpenStair`) is 1.75 m wide like the Wide Stair, but steeper (10 steps of 0.3 m over `StairRun` 2.9 m) and open:
  steel treads with yellow and black nosings on slim stringers, hand rails on posts, and only a hidden block under each tread for
  the feet, so the space under the flight is free (two crates stand under its high end). Its head is where the hall's rail ends
  (Karl: the deck floor whole beyond it, a 1 m landing to the galley's wall). It says `"collider": "boxes"`: as a hull round its
  stringers it filled the flight and nobody got past the first step. Its foot is 0.05 m under the deck before the opening
  (`StairLead`): on a flight this steep, a head that steps up near the opening's fore edge meets it.
- **Catwalks in its style** (Sean, 2026-11-21): the stair has a `head` socket (+z, on a header angle under its top tread) that
  takes any open end of a Steel Catwalk (`CatwalkParts`, Constructs/Parts/Catwalks: runs of 1.5, 3 and 6 m, a Corner, Tee, Cross and
  End, and a Leg that stands a deck 3 m over a floor, level with the stair's head). The same slats, striped nosings, slim channels
  and rails on posts (a knee rail added); hidden guards along the rails keep a walker on the deck. Hung instead of stood (Sean):
  a Catwalk Hanger (2.5, 4 or 6 m from the ceiling to the deck) is a crossbar under a pair of leg sockets on two rods to ceiling plates.
- **In the game** (Karl, 2026-11-20, "add to game"): its pieces have Assembling recipes like the Rust Bucket's; it is spawned from
  the Blueprints tab (no start uses it).
- **Checks** (2026-11-20): `--check-vehicle` 0 coplanar faces between parts, both drives' works at 100%; `--selftest-only "No
  z-fighting"` and `"Assembling"` clean; `--flight-check` flies (lift 1.66x weight). `--check-building`: the stair is climbed
  standing; with PR 381's check fix the whole ship passes.
