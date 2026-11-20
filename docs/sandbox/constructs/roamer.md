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
- **Checks** (2026-11-20): `--check-vehicle` 0 coplanar faces between parts, both drives' works at 100%; `--selftest-only "No
  z-fighting"` clean; `--flight-check` flies (lift 1.64x weight). `--check-building` fails the same 26 lines as the Rust Bucket on
  main (the Wide Stair's head), which it shares.

## Not yet

- Not in any start; spawn it from the Blueprints tab. Its pieces have no Assembling recipes (`Roamer` is in Assembling's
  SkipFolders while it is a draft).
