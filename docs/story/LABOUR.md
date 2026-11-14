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

## The day

Karl (2026-11-14: make the colony interesting): the colony keeps a timetable, so it changes as you play. The file's
`"day"` lists the periods in order, each with its length in real minutes and the tannoy's call; it runs round from the
morning count while the colony stands (47 minutes a round today):

| Period | Minutes | Who is out |
| --- | --- | --- |
| Morning count | 4 | the roll call on the muster square, those waiting to be counted, the records office queue, the bus stop |
| Morning work | 12 | the work gangs: kerbs, snow, the street, the escorted column, the rock pile, the records office queue, the bus stop |
| Dinner | 6 | the canteen queue, and inside four mess tables of diners on stools over tin bowls |
| Afternoon work | 12 | as the morning |
| Exercise | 4 | single file round the muster square, a pace apart, hands behind their backs; those waiting to be counted |
| Evening count | 4 | the roll call again |
| Lock-up | 5 | nobody but two night patrols with torches, one up and down the street and one round the inside of the wire |

A gang names its periods in `"when"`; one without works all day (the quarry's, and every gang a labour post names in
`Story/Posts`: the boiler coal, the sick parade, the brazier and the heating main, so the story people working there are
always somewhere; a post's gang that is off shift has no place, `LabourSceneOf`).

- **The host** (`Source/Game/GameLabourDay.cs`) works out the period at each of its looks (every 2 s), and at a change
  sends `Msg.LabourDay` (protocol 316: the period and a bit per scene on shift, also to each joiner) and the tannoy's
  call as a line in the chat of everyone within 140 m of the colony who is past the opening's scenes. Every PC's
  nearest loudspeaker chimes (`PartAmbience.AnnounceSoon`).
- **Every PC** (`LabourRender.Shifts`) lets a gang go off, or come on, only while no part of it could be in view: its
  bounds outside the view or past the far range. Look away and the colony has moved on; nobody pops in or out in front
  of you. Just after the list arrives (a joiner) the shift is taken at once.
- Nothing is saved: a loaded world starts at the morning count.
- At most 120 people on shift at once and 220 in the file (the parser says which period is over).
- **Trying it:** the window tool's `period NAME` jumps the day to a period (and lets the gangs change in view for a few
  seconds), and `gang NAME SIDE,UP,OUT` puts the eye beside a gang looking at it:
  `--window-shot OUT.png --survival --calm --map Glacier --do "key Jump; wait 40; period Dinner; gang Dinner, first table 0.3,0.6,2.2; wait 5"`.

## Incidents

Now and then something happens in a gang in front of you (Karl, 2026-11-14: make the colony interesting). Shown, not
told: the gang's place stands still while it plays (a walking file halts, a queue stops shuffling), the others turn
their heads, and the guard says one thing to those within 22 m, then a second.

- **Collapse** (21 s): one inmate goes to their knees and sits slumped in the snow, rocking; the guard walks over and
  stands over them pointing ("On your feet."); they get up and the gang goes on. Roll call, queues, standing, shovels,
  picks, hammers and the march.
- **Scuffle** (17 s): two side by side turn and shove each other; the guard comes between them, and they stand with
  hands on heads until let go. Roll call, queues, standing and huddles (two or more inmates).
- **Search** (17 s): the guard walks up, pats one down (arms out), holds something up to the light, pockets it.
  Roll call, queues, standing, huddles and the march.
- **When:** the host looks every 2 s; once 75 to 140 s have passed since the last (60 s after the colony stands), it
  picks for one player the nearest gang within 24 m in front of them (dot 0.55) that is on shift, has a guard, has no
  story person working in it and isn't already in one. Never during the alarm (the break-out) or in the opening's
  scenes.
- **The guards' words** are `Story/Labour/Incidents.json` (`call-of-slop-incidents/1`): per kind a "first" list and a
  "then" list, one line picked by the incident's tick, 1 to 100 ASCII characters. The speaker is "Sergeant" at the roll
  call, "Foreman" for a foreman's gang, else "Officer". A bad file is refused with a `[labour]` line and there are no
  incidents.
- **Wire:** one `Msg.LabourIncident` (u8 scene, u8 kind, u8 slot, u32 the tick it began) to everyone; every PC poses
  from it (Render/LabourRender.cs `IncidentPlace`, `IncidentPose`). A joiner during one sees the gang as normal. Nothing
  saved.
- **Trying it:** the window tool's `incident GANG KIND` starts one now:
  `--do "skip induction; skip duty; period Morning count; wait 200; incident Roll call collapse; wait 300; gang Roll call 0.5,4.5,2.5; wait 2; shot"`.

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
- **Kinds:** stand, shovel, pick, hammer, haul, barrow, queue, rollcall, huddle, march, sweep, paint, sit, eat (down both
  sides of a table given by `"part": "Mess Table"`, its length along the gang's right: a stool, a bowl and a spoon each)
  and patrol (officers only, round a way with a torch whose lens is drawn lit). A march with a gap over 1.2 m walks with
  hands behind the back instead of a hand on the shoulder in front. A new kind is an entry at the end of `LabourKind` (a
  wire byte), a layout in `Spot`/`Place` and a pose in `PoseOf`.

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

- **Server and network:** no work per tick (one look every 2 s at the colony's and the quarry's construct ids, the
  day's period and, once a minute or two, the gangs in front of each player for an incident) and one message of about
  800 bytes when the colony stands and to each joiner; then 10 bytes at each change of period and 8 at an incident.
- **Incidents:** the three to five figures in one are posed every frame within 32 m while it plays (17 to 21 s); the
  rest of the gang as before.
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

- The walking gangs' ways are routed round the buildings on the bots' site graph once it is built (PATHS.md), so a
  barrow no longer runs through a wall; a gang's spot isn't moved.
- They don't step aside: walk through one and you go through it. They can't be shot or talked to (the story people
  working among them can: "Hands" above).
- The march and queues only shuffle on their spots; nobody walks into a building (the diners are there when you look
  in, the queue gone when you look out).
- Murmured lines when you pass (the story people's babble, GameStoryBabble.cs) would suit the queues.
- The story people's own posts don't follow the day yet (the warden's duty desk is kept through the night).
