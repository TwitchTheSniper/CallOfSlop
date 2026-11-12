# Uniforms

Faction uniforms for people's bodies (Karl, 2026-11-12: Ministry of Slop clothing, Cold War style, by rank). Cosmetic
only: a uniform changes how a body is drawn, never its hit boxes.

- **Data:** `Cosmetics/uniforms.json` (`call-of-slop-uniforms/1`), loaded by `Source/Cosmetics/Uniforms.cs`. A uniform's
  number on the wire is its place in the file: add new ones at the end, never reorder.
- **What a uniform is:** colours for the coat (shirt, shoulders), sleeves, collar, trousers, the hips (the pelvis box,
  so a greatcoat reads as one down to the thigh), boots, gloves and cuffs; up to 30 torso boxes laid where the plate
  carrier was (coat body, belt, buckle, lapels, buttons, epaulettes, pips, chevrons, badges, ribbons, collars; the shirt's
  abdomen, chest, shoulders, collar and neck stay, in the coat colours); and a hat (hat parts as in `hats.json`, drawn
  and knocked off like a prestige hat, `HatRender.HatFor`). `"detail": true` boxes are dropped beyond 30 m.
- **Frames:** torso boxes are in the standing torso's frame (centre 0, x +-0.4 at the shoulders, y +-0.25, -z the front:
  the chest's face at z -0.21, the coat body's at -0.215, the back at +0.21; the shoulder box's top at y 0.275). Crouch
  and prone scale them with the torso. The no-coplanar-faces rule applies: 2 to 5 mm between faces that touch.
- **Who wears what** (`Source/Game/GameUniforms.cs`, host, once a second in the story's tick): a story person (a bot)
  wears the first uniform whose `wornBy` starts their name ("Warden ", "Officer ", "Riot Officer "). A player in a world
  with the story's opening (the penal colony) is issued the uniform whose `tiers` holds their standing tier with its
  `faction`, unless they picked **Own Clothes** (`LookAxis.Uniform` on the APPEARANCE screen). A memo goes out under the
  faction's voice when one is issued or withdrawn. Plain Sandbox worlds have a story too (everyone an Inmate there), so
  players get nothing outside the opening's worlds.
- **Wire:** the roster carries one byte a player (bots too) after the look; the look itself is 4 bytes (the fourth is
  the uniform choice). Protocol 256.
- **Drawing:** `BodyModel.UniformOf(p)` (null in a robot shell), `ColorsFor`, `LegsFor`, `ArmsFor`; `BodyModel.Build`
  takes the uniform. The rig, the immediate path, ragdolls and gibs all go through these.

## The Ministry of Slop's (2026-11-12)

| Uniform | Who | Look |
| --- | --- | --- |
| Ministry Issue Boiler Suit | Inmate | mustard drab suit, zip, stencilled number patch, MINISTRY PROPERTY on the back, watch cap |
| High-Visibility Boiler Suit | Person of Interest | orange suit with reflective bands, "for your own visibility" |
| Trusty's Boiler Suit and Flat Cap | Trusty | the suit with a red brassard, a leather belt, a pencil, tweed flat cap |
| Ministry Contractor's Donkey Jacket | Contractor | navy donkey jacket, PVC yoke, brass contractor badge, white hard hat |
| Crown Servant's Overcoat and Bowler | Crown Servant | charcoal Chesterfield, velvet collar, white shirt and maroon tie, crest pin, bowler |
| Colony Officer's Greatcoat | "Officer " | grey-blue double-breasted greatcoat, Sam Browne strap, one pip, crest badge, fur ushanka |
| Colony Sergeant's Greatcoat | "Sergeant " | the officer's coat with two chevrons on each shoulder and a ribbon |
| Warden's Greatcoat | "Warden " | black greatcoat, astrakhan collar with red tabs, gold epaulettes with two pips, ribbons, peaked cap with a red band |
| Riot Officer's Kit | "Riot Officer " | black padded coat, chest and back plates, numbered, steel helmet with visor |
| Foreman's Donkey Jacket | "Foreman " | donkey jacket with an orange hi-vis yoke, yellow hard hat with a lamp |
| Senior Civil Service Suit | "Permanent Secretary ", "Licensing Officer " | navy suit, tie, watch chain, bowler |

Enemy of the State is issued nothing. Ideas left: uniforms for the other factions (IT Department, Clan MacSlop, the
call centre), rank insignia that follow the player's own faction ranks.
