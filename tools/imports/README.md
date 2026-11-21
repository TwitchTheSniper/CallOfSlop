# Imported ships

Ships designed in another tool (a three.js page exported as glTF) and turned into game parts and a blueprint.

- One folder per ship: `ship.glb` (the model) and `import.json` (how to read it: turn, raise, roles, fittings, wires, `move`
  for furniture to shift, and `gallery`).
- `--gen-parts imported_ships` (Source/PartKit/Generators/ImportedShips.cs) writes `Constructs/Parts/Imported/<Prefix>/` and
  the blueprint. Rebuild, then check it: `--check-vehicle`, `--flight-check`, `--check-building "NAME"`.
- `--imported-gallery DIR` writes the Imported ships gallery (`viewer.html` here, `models/index.json`, one `models/NAME.json`
  a ship), republished as the Imported ships Artifact https://claude.ai/artifact/5szc9SYyS2vMJz7jnkzWDe with the three files.
  `gallery` in import.json says what the cutaway hides (`roof`), what is hull (`hull`), and the buttons that work doors
  (`controls`: a label, a `match` on model object names, the shut and open words).
- The brief for the designing AI is the project doc "Ship design spec for Call of Slop"
  (https://claude.ai/artifact/PmYRSrR1VQr8XiHjuE2Wgt).
