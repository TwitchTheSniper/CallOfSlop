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
  sends the list once in `Msg.Labour` (protocol 276) and to each joiner. A bad file is refused with a reason (the log's
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

## Hands

A story person can work in a gang (GameLabourHands.cs; the break-out's convicts, BREAKOUT.md). A labour post in
`Story/Posts/PenalColony.json` (`"at": "labour", "gang": NAME`, only a gang that stays put: shovel, pick, hammer,
huddle, sit, stand; one post a gang) sends a person on a Work step to the gang's last inmate's place. While they stand
there working (not talking, reacting or fighting), the host lists them in `Msg.LabourHands` (protocol 286; sent only
when it changes, and to joiners): every PC then draws them as that gang's last inmate, in their own body's look, and
not their bot body, and the gang's own last figure stands aside. Anything else (a player says hello, they come to look
at a stranger, the shift ends) shows the body again, and the gang is one short until they come back.

## Cost

Karl wants to keep the game's thousands of frames a second.

- **Server and network:** no work per tick (one look every 2 s at the colony's and the quarry's construct ids) and one
  message of about 600 bytes when the colony stands and to each joiner. Nothing after.
- **Drawing:** all in one instanced batch. Within 13 m a full body (about 45 boxes: the players' own torso, head, legs,
  mittened arms and the tool), to 32 m about 19, to 110 m seven or eight, past that or off screen nothing. Within 9 m a
  figure is posed every frame; further off its boxes are kept and drawn again, and it is posed 45, 30 or 15 times a
  second by distance. At a high frame rate the cost is handing the kept boxes to the batch.
- **Measured** in the cloud container (one slow core shared with llvmpipe drawing the window, so slower than a desktop):
  posing a figure 3 to 8 us; handing a box to the batch about 70 ns. In the densest views (standing in the roll call
  with the column passing: 30 to 55 figures in view, 850 to 1,300 boxes) that is 60 to 85 us a frame plus the poses due
  that frame. Not measured on a real GPU: the boxes are the same instanced cube the constructs use, so the GPU side
  should be small. F4 > People (with Engine ticked) > "Convicts" turns them off for everyone, and its tooltip shows the
  last frame's figures and boxes.

## Not done / ideas

- They don't step aside: walk through one and you go through it. They can't be shot or talked to (the story people
  working among them can: "Hands" above).
- The march and queues only shuffle on their spots; nobody goes into a building.
- Murmured lines when you pass (the story people's babble, GameStoryBabble.cs) would suit the queues.
- A gang's work could follow the story's clock (night shift, meals) instead of running all day.
