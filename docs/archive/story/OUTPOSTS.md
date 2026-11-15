# Outposts: the factions out in the world

The factions' small places beyond the colony (the user, 2026-11-13: "faction presence beyond the colony", with performance
a hard requirement). Each outpost is one faction's: a building or two in its own style, and a few people at posts or on a
foot patrol, who talk to a player by their standing with the faction, and fight the ones it hates. Glacier has five round
the penal colony; another world gets its own file.

| What | Where |
| --- | --- |
| The data, one file a world | `Story/Outposts/*.json` (format `call-of-slop-outposts/1`; the header of `Glacier.json` lists every field) |
| The loader | `Source/Game/OutpostData.cs` (a bad file is refused with a reason, and that world has no outposts) |
| The game's side | `Source/Game/GameOutposts.cs` (host only, from the story's tick) |
| The buildings | `Source/PartKit/Generators/BuildingParts.Outposts.cs` (`--gen-parts building_parts`; styles in docs/sandbox/constructs/buildings.md) |
| The hand check | `--outpost-check` (`Source/Game/OutpostCheck.cs`): a throwaway story world, each outpost visited in turn |

## Glacier

| Outpost | Faction | Where (from the colony's gate) | Building | People |
| --- | --- | --- | --- | --- |
| Checkpoint Four | Ministry of Slop | east, 600 m | Ministry Checkpoint (booth, barrier line, lit flag) | Sergeant Loame and Officer Tidy at the barrier |
| Sentry Post Nine | Ministry of Slop | north-west, 750 m | Ministry Sentry Post (pillbox behind sandbags) | Sergeant Pike at the post; Officers Plum and Gorse walk the perimeter, opposite ways round a 300 m loop |
| The Wee Watch | Clan MacSlop | south-west, 1,050 m | Clan MacSlop Bothy (stone, tin roof, casks, saltire) | Dougie the lookout, Kirsty at the casks, Wee Wullie the piper going round the camp |
| Relay 0800 | The Call Centre | north-east, 850 m | Call Centre Relay (purple portacabin, phone mast, prize wheel) | Darren at the loudhailer; Tracey walks out to whoever passes |
| Field Cabinet NC-14 | IT Department | south-east, 720 m | IT Field Cabinet (beige steel box, mast, dish) | Gary at the door; Priya walks the cable |

They keep clear of the colony's site, the quarry (200 degrees, 260 m), the traffic's placeholder stations (all within
420 m) and the spaceport being built about 2 km out.

## How it works

- **Placed by sampling the ground.** An outpost's spot is its bearing and distance from the colony's gate (or a latitude
  and longitude on a planetoid), moved a few degrees round or a little nearer or further until the ground within its
  `"flat"` is level to a metre (eight points on a ring against the middle). The same ground gives the same spot every
  load, so a new terrain (ridges, crevasses) moves an outpost only on a fresh world.
- **Built once.** The first time the story comes to the world, each outpost's buildings are built, one every quarter of a
  second, and a story Thing (`outpost:NAME`) notes the outpost. A world saved before the outposts existed gains them the
  first time it is played again; a world that has them only finds them again (by where they stand) and claims them.
  Every building is the faction's (`Claims`): wrecking or stripping one is that faction's deed.
- **People only near a player.** An outpost's people get bodies (bots of the people's side, `SbPeopleTeam`) when a player
  comes within the file's `"wake"` (150 m on Glacier, plus how far its patrols go), one body a look, and lose them three
  seconds after nobody is within `"sleep"` (200 m). A patrol's body comes back where the clock says it has got to round its
  loop (a stroll of 1.6 m/s and its stand at each spot), so the walk looks continuous to anyone who comes and goes.
  At most `Game.OutpostBodiesMax` (8) bodies in all; they don't take from the story people's own eight.
- **Standing decides the welcome.** A player who comes within 12 m hears a line by their standing tier with the faction:
  the lowest tier is `"hostile"`, the next `"cold"`, the middle (where a stranger starts; the Ministry's Inmate) `"neutral"`,
  anything above `"friendly"`. Once every four minutes an outpost, a player. The hostile (and, at the Ministry's, anyone the
  colony's lockdown is after, or anyone Notorious) are warned from 30 m and shot at three seconds later by everyone there,
  until they are 60 m off or dead, or 45 s have passed quietly.
- **Hurting one** turns the whole outpost on whoever did it; **killing one** is the faction's "Killed our staff" (each is a
  story Person, `MemberOf` the faction, the same one each time they come). Someone killed is relieved after `"relief"` s
  (15 minutes) once nobody is near.
- **Talking.** The talk panel works on them (F on the body): hello by standing, and the place, themselves, advice and news
  from the file. Someone they would rather shoot gets a `"snub"`. They stop and face whoever talks to them.
- **Now and then** an outpost with a player within 25 m says one of its `"idle"` lines, every 50 to 90 s.
- **Dress.** A name that starts with a title wears that title's uniform (`Officer `, `Sergeant `: Cosmetics/uniforms.json);
  the other factions have no uniforms yet.

No wire change: bodies are players, buildings are constructs, lines are chat (and murmur like any story person's).

## Costs

Measured by `--outpost-check` in the cloud box (2 cores, headless):

- **Far outposts** (the usual case): the host's look is 1.0 to 1.6 us a call, four calls a second (about 5 us of CPU a second), nothing allocated. That is
  every outpost on the world, with nobody near any of them.
- **An awake outpost**: its people are bots (two or three at a time) with a plain brain: walk to the post or the next spot,
  face out, turn to a talker. On the 2-core cloud box the server's whole update was 45 to 52 ms of CPU a second with an outpost awake against 55 to 60 ms with none: the bodies are lost in the box's noise. An outpost's look with bodies is a few distance checks per player and body.
- **Buildings**: five small blueprints (15 to 44 steps), built once, one a look. Far ones are culled or drawn as one box
  like any construct (docs/render; `ConstructRenderCull.cs`).

## Adding outposts

1. Write the faction's building style in docs/sandbox/constructs/buildings.md first, then its blueprints in
   `BuildingParts.Outposts.cs` (`--gen-parts building_parts`, then `--check-construct-parts`, which builds every blueprint).
2. Add a site to the world's file (or a new file with `"map"`). Keep sites a few hundred metres apart and clear of the
   colony's site, its quarry and any other place being built.
3. Run `--outpost-check`: every outpost should be placed, built, wake with all its people, say something, and sleep
   again; the clan's should fight a player in blood feud.

## Not done

- Other worlds have no outposts yet (each a `Story/Outposts/<World>.json`, by latitude and longitude where there is no colony).
- Patrols walk straight lines between spots (no nav graph on streamed ground): give routes open ground.
- Outpost people are not the story's agents: they don't remember players, gossip or travel. Their lines are the file's.
- No uniforms for the Clan, IT or the Call Centre; no vehicles (the vehicle system is being redone).
