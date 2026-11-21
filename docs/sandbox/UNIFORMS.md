# Uniforms

Uniforms for people's bodies. Cosmetic only: a uniform changes how a body is drawn, never its hit boxes. The only one
now is the Space Suit (docs/sandbox/EVA.md), drawn over whatever a player wears while they have one on. The Ministry of
Slop's uniforms (2026-11-12, issued by story standing) went with the story and its factions (2026-11-21,
docs/removals-notes.md).

- **Data:** `Cosmetics/uniforms.json` (`call-of-slop-uniforms/1`), loaded by `Source/Cosmetics/Uniforms.cs`. A uniform's
  number on the wire is its place in the file: add new ones at the end, never reorder.
- **What a uniform is:** colours for the coat (shirt, shoulders), sleeves, collar, trousers, the hips (the pelvis box,
  so a greatcoat reads as one down to the thigh), boots, gloves and cuffs; up to 30 torso boxes laid where the plate
  carrier was (coat body, belt, buckle, lapels, buttons, epaulettes, pips, chevrons, badges, ribbons, collars; the shirt's
  abdomen, chest, shoulders, collar and neck stay, in the coat colours); and a hat (hat parts as in `hats.json`, drawn
  and knocked off like any hat, `HatRender.HatFor`). `"detail": true` boxes are dropped beyond 30 m. A hat part with `"glass": OPACITY` (0.05 to under 1) is glass, drawn blended after the hat's other boxes and the face behind it (the space suit helmet's visor); ragdolls leave it out.
- **Frames:** torso boxes are in the standing torso's frame (centre 0, x +-0.4 at the shoulders, y +-0.25, -z the front:
  the chest's face at z -0.21, the coat body's at -0.215, the back at +0.21; the shoulder box's top at y 0.275). Crouch
  and prone scale them with the torso. The no-coplanar-faces rule applies: 2 to 5 mm between faces that touch.
- **Who wears what:** a player in a space suit (`Uniforms.SpaceSuit`). The APPEARANCE screen's uniform choice is hidden.
- **Wire:** the roster carries one byte a player (bots too) after the look; the look itself is 4 bytes (the fourth is
  the uniform choice). Protocol 257.
- **Drawing:** `BodyModel.UniformOf(p)` (null in a robot shell), `ColorsFor`, `LegsFor`, `ArmsFor`; `BodyModel.Build`
  takes the uniform. The rig, the immediate path, ragdolls and gibs all go through these.

Ideas left: uniforms for the crew powers, rank insignia that follow the player's own faction ranks.
