---
paths:
  - "Source/Story/**"
  - "Source/StandIns/**"
  - "Source/Game/GameStory*.cs"
  - "Source/Game/GameColony*.cs"
  - "Source/Game/GameLabour*.cs"
  - "Source/Game/GameTutorial*.cs"
  - "Source/Game/Tutorial.cs"
  - "Source/Game/GameOutposts.cs"
  - "Source/Game/GameSpaceport.cs"
  - "Source/UI/UiStory*.cs"
  - "Story/**"
  - "docs/story/**"
  - "docs/sandbox/TUTORIAL.md"
---

# Story, the colony and the tutorial: what went wrong here before

`docs/story/STORYLINE.md` is the storyline (read it first); `docs/story/STORY.md` is the engine. Each line below is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- 2026-11-11: the induction's "Pick up a part" step dropped Frame Blocks, which are physics-only (welder-fitted), so F refused them and the tutorial stalled at step 8. A part a tutorial or duty hands to the player is checked against the rule that uses it (`PhysicsOnly`, the backpack's slots) through the real call (`ConstructEdits.PickUp`, `Stow`), not by its look.
- 2026-11-13: the story people had no bodies on the colony at all: their middle is the spot outside the gate, and the covered way laid there made `SandboxSpawnSpot` refuse it ("on a construct"), so `StoryMiddle` gave up quietly (a system that gives up says so in the log). A piece laid round the colony is checked against the spots the colony's systems stand on (`_opGate`, the story middle): run `--traffic-check 15` and grep `[story]` for the people's bodies; stand spots accept a low deck with open sky (`StoryStand`).
- 2026-11-13: the break-out never began in a real play-test: the storylet runner gives a player one storylet at a time, and the Ministry's first duty held it, so a road meant to be taken "at any point" waited behind it. A side road that must run beside the main quest goes in the `side` bucket (its own lane); try a new road with a duty under way (`--storylet-sim`).
- 2026-11-14: a verb whose effect named a storylet quality ("actor Fixit =2") refused the whole of Verbs/Storylets.json, and with it every storylet: the verbs load before the storylets declare their qualities. A storylet quality is set by a storylet end or by the game (`World.SetQuality`), never by a verb's effect; run `--check-story` after any verb change.
