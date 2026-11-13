# The colony's convict labourers

Karl (2026-11-13): the story's start should feel populated, with more convicts in the Ministry of Slop's penal colony on
Glacier, doing things. The colony's named people (the warden, the guards, the foreman) are story people with bodies
(STORY.md "Bodies"); round them now work about eighty nameless convicts and their guards, shown and never told, the
labour grim and the Ministry's way with it absurd.

## What is there

`Story/Labour/PenalColony.json` lists the gangs (the file's header says every key). Today:

- **The colony:** a morning roll call of two ranks on the muster square (each in turn answers "Present" with a hand up
  while the sergeant walks along with a clipboard), three waiting under the muster roof in the yard, queues at the
  records office and the canteen (every so often it moves up one and the one at the front goes round to the back), a
  sick parade sat outside the infirmary (a bandaged head, an arm in a sling), coal shovelled and barrowed to the boiler
  house, kerbstones painted white on their knees in the snow, the pavement shovelled into the street while a sweeper
  sweeps it back, a column shuffling up and down the street with a hand on the shoulder in front under escort, four at
  the crew bus stop the bus never stops for, a huddle at the heating main's valve chamber, another round a brazier, and
  a pile of rocks carried to another pile and back while the foreman counts both.
- **The quarry:** picks at the rock either side of the mine face (the player's own spot left clear), spoil barrows full
  out and empty back, sledgehammers round a heap making small rocks, a brazier for the ones on their break.
- **Life:** the cold ones stamp their feet, blow into their hands and look round; workers stop now and then to lean on
  the tool and straighten their back; picks, hammers and shovels are heard within 16 m.
- **The alarm:** while the colony's lockdown alarm sounds (GameColonyLockdown.cs) every inmate kneels with their hands on
  their head and the guards stand ready; work picks up where it stopped when it ends.

## How it works

They are pictures, like the stand-in craft: no AI, no bot, no physics, no collision, nothing saved, no hit boxes (a shot
goes through them).

- **The host** (`Source/Game/GameLabour.cs`) reads the file, and once the colony stands (and again when the quarry does)
  works out each gang's points in the world from its frame: `colony` is the Penal Colony construct's own metres, a
  building its blueprint's (with `part`, from that part), `quarry` the Mine Face's (x along the face, z out from it). It
  sends the list once in `Msg.Labour` (protocol 269) and to each joiner. A bad file is refused with a reason (the log's
  `[labour]` line) and there is nobody.
- **Every PC** (`Source/Render/LabourRender.cs`) builds the figures when the list changes: the players' own torso and head
  (`BodyModel.Build`) in a Ministry uniform (mostly the issue boiler suit, a little lighter or darker each, the odd
  high-visibility suit, now and then a trusty's flat cap; guards in the officer's or the sergeant's greatcoat, the
  quarry's in the foreman's jacket), the legs' boxes by two-bone IK, mittened arms by IK, and a tool. Each is posed from
  the server tick by a short script per kind (keys eased between), so every PC sees the same stroke at the same moment.
  The ground under each spot and along each way is found once by a short ray (and again at 3, 12 and 40 s, for the
  constructs that arrive after the list); nothing casts per frame.
- **Kinds:** stand, shovel, pick, hammer, haul, barrow, queue, rollcall, huddle, march, sweep, paint, sit. A new kind is
  an entry at the end of `LabourKind` (a wire byte), a layout in `Spot`/`Place` and a pose in `PoseOf`.

## Cost

Karl wants to keep the game's thousands of frames a second. The labourers cost no server tick (one look every 2 s for
the colony's construct ids) and no network after the one message (about 30 bytes a gang). Drawing: up to 42 m a full
body of about 40 boxes, to 120 m a body of eight, past that or off screen nothing; all in one instanced batch. Measured
under xvfb (llvmpipe, the container's 4 cores): see the PR for the numbers.

## Not done / ideas

- They don't step aside: walk through one and you go through it. They can't be shot or talked to.
- The march and queues only shuffle on their spots; nobody goes into a building.
- Murmured lines when you pass (the story people's babble, GameStoryBabble.cs) would suit the queues.
- A gang's work could follow the story's clock (night shift, meals) instead of running all day.
