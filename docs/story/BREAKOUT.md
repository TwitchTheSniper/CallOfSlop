# The break-out

Karl (2026-11-13): a road off the main quest that can be taken at any point in the penal colony. The convicts doing the
colony's chores can be spoken to; a few of them know of a break-out, and the player slowly earns their trust. It leaves
by stealing a company transport to the Ministry Spaceport, fighting through its guards (with scripted events: friends
dying, last words that tell the player something they will need later), and getting off the planet in a ship a few of
them can board.

Built in slices, one PR each:

1. **The crew** (this file's first part): four convicts in the gangs, talking, trust, the question.
2. **The night**: the crew takes the Ministry's ration hauler to the spaceport ("The night" below).
3. **The spaceport**: the guards, the deaths and their last words into the Logbook (not built yet).
4. **The ship**: a stand-in ship the survivors board, off the planet (not built yet).

## The crew

Four convicts in the Penal Colony cast (`Story/Casts/PenalColony.json`), in the Ministry's boiler suit
(`Cosmetics/uniforms.json`, `wornBy`), each working in a gang of the convict labourers (LABOUR.md "Hands") through a
labour post (`Story/Posts/PenalColony.json`):

| Who | Where | What they bring |
| --- | --- | --- |
| Dot Pruett | the boiler coal | drove the Ministry's ore haulers: the transport |
| Wren Haskett | sick parade | cleaned the Ministry Spaceport for eleven years: its rota; once cleaned a room under headquarters with five big chairs (`frag.silence`) |
| Ambrose Thale | the brazier | built and flew hulls: the pilot; his gran kept a lottery ticket (`frag.threat`'s seat lottery) |
| Hester Mossop | the heating main | decides who comes; Bellweather caught her once and waits for twice |

While at their post they are drawn shovelling or huddled with the rest of the gang; walk up and press F and they stop
work and face you. A stranger gets brush-offs; each proper answer warms them a little (`Story/Topics/Breakout.json`),
and so does saying hello now and then. Their own lines are `Story/Lines/Convicts.json`.

## The beats

`Story/Storylets/Breakout.json` (B plot, so it runs beside whatever the main road is doing; DRAFT words). The quality
`Crew` (0 to 6) is how far in the player is.

1. *Sized up* (Dot likes you 10): if Crumb asks who's been at the coal? "Nobody" is Crew 1.
2. *The parcel* (Dot 15): carry something to Wren without being searched (it is Contraband, so a shakedown finds it).
3. *The delivery*: say hello to Wren with it still on you: Crew 2. Lost in a search, or 40 minutes late, Dot cools.
4. *The rota* (Wren 20): the spaceport changes its guard on the hour, except at four in the morning (lead
   "Spaceport Rota"): Crew 3.
5. *A seat and a stick* (Ambrose 20): he can fly them off (lead "A Pilot"): Crew 4.
6. *Mossop's question*: in (Course Escapee, Crew 5) or out (asked again in 45 minutes). A trustee can say yes too, and
   leaves the Ministry road for it.
7. *The night*: Dot names the transport (lead "The Night") and its hook, `breakout`, starts the night below.

Grassing: once Crew is 1, Sergeant Bellweather offers "Some of the inmates are planning something." Telling him sets
`Grassed` (Suspicion -3, his liking up, Mossop's and Dot's grudges), and *Searched* closes the road (Crew 0).

## The night

`Game/GameBreakout.cs`, data `Story/Opening/Breakout.json`.

- **The muster.** The crew who are alive walk to the Ministry's ration hauler (a STAND-IN hover hauler, Source/StandIns)
  waiting outside the colony's main gate (`motorPool`, the colony's frame). The player gets a panel top left ("THE
  BREAK-OUT") and the orange marker on the hauler. One hauler is shared by everyone mustering, and called again if it
  tires of waiting (15 minutes).
- **Away.** Walking up to it (6 m) is boarding: Dot says her line, the colony's siren wails behind them for 75 s (the
  convicts kneel; no riot squad: that is the spaceport's job), and a memo goes round. Anyone else standing at the hauler
  rides too. The ride is the crew bus's scene (Program.Quarry.cs: the camera follows the hauler away, black, and watches
  the other come in), with the file's `caption`.
- **The spaceport.** Everyone who rode is set down beside the second hauler at `landing` (the spaceport's frame, in
  front of the tower), the crew round them. The story records the player's `Abscond` (the Ministry's deed "Absconded",
  -30) and their `Escaped`, and each convict who came gets `Fled`: their bodies keep to the landing from then on. The
  marker moves to the Ministry Courier on the first pad.
- **Saves.** Nothing new is saved: a player with the lead who hasn't `Escaped` is back at the muster when the world
  loads, one who has is at the spaceport, and a convict who `Fled` is put back at the landing.
- **Wire.** `Msg.Breakout` (protocol 288) to each player: their stage, the marker, the ride's tick and the two haulers.
