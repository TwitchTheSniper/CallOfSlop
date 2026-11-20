# The Roamer (the rust bucket's camper sister)

Karl (2026-11-20): "another Rust Bucket style ship? Glass, sort of like a winnebago styling similar to the panels it has."
Generator: `Source/PartKit/Generators/RoamerParts.cs` (`--gen-parts roamer_parts`), which writes `Constructs/Parts/Roamer/*.json`
and `Constructs/Blueprints/Roamer.json`. Change the generator, never its output. Draft: shown on the Ship gallery (`#roamer`).

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
- **The rear** (Karl, 2026-11-20: "the rear contour space needs to be taken up a little more and sealed"): the rear cap sits about
  1 m closer to the rooms, the rooms' back wall is drawn, and the tail behind it is filled (`Fill`), so no cavity shows behind the galley.
- **The stair** (Karl, 2026-11-20, from a picture: an open steel stair, "we want to be able to shove stuff under the stairs"): the
  Roamer Stair (`OpenStair`) has the Wide Stair's footprint and steps but is open: steel treads with yellow and black nosings on
  slim stringers, hand rails on posts (the port one stops two steps short, where the top steps step off into the hall), and only a
  hidden block 0.2 m deep under each tread for the feet, so the space under the flight is free. Two crates stand under its high end.
  It says `"collider": "boxes"`: as a hull round its stringers it filled the flight and nobody got past the first step. Its foot is
  0.25 m under the deck before the opening (`StairLead`, as the Rust Bucket's after PR 381).
- **Checks** (2026-11-20): `--check-vehicle` 0 coplanar faces between parts, both drives' works at 100%; `--selftest-only "No
  z-fighting"` clean; `--flight-check` flies (lift 1.66x weight). `--check-building`: the stair is climbed standing; the deck and
  doorway lines that fail are the ones PR 381 fixes in the check itself (re-run once it is on main).

## Not yet

- Not in any start; spawn it from the Blueprints tab. Its pieces have no Assembling recipes (`Roamer` is in Assembling's
  SkipFolders while it is a draft).
