# The storyline as it stands

The one written-down version of the main story. Karl steers it in plain words; Claude writes the data. To read what is
in the game, open the story map (https://claude.ai/artifact/JJLq8Be3fd21fRvtdS22EF). When something here changes,
change it here and nowhere else. Last settled: 2026-11-11 (the Light and the Gallery).

## The story

- **Then.** Something local was going to swallow the five systems: a black hole. They banded together, each solving
  one part, and folded the space around the planets, the threat included. The fold did not stop: it ran out to infinity
  and took the rest of the universe. An accident of good intentions.
- **Between.** Nobody undid it. The records were withdrawn. Nobody from then is alive, and there is no villain.
  What is left in the sky is the Light (below): the one thing everybody can see and nobody understands.
- **Now.** The player crash-lands, wakes in the Ministry of Slop's penal colony, and uncovers the past from five
  abandoned places, one per system, in any order. The conflict is among the living, over what to do with what is dug up.
- **The end.** At the machine in the Gallery (below), with nobody there to argue with: switch it off (the universe
  returns, so does the black hole, the planets fall); build a delay first (a short last round to see the people met and
  choose where to stand, then it stops); or keep it (the five go on, everything else stays folded away).
- **Underneath.** What is life, is yours worth more than everything folded away, what gives it meaning. The people met
  along the way are part of the price.

## The five records

Each is one fragment (`Story/Fragments/Fragments.json`; the ids never change) in one place. The place tells the story
and the fragment is the last thing found in it: every room answers "what were people doing here on the last day?", and
each place has one big machine the player brings back to life by wiring power into it.

| Fragment | Answers | Place | Its machine |
| --- | --- | --- | --- |
| `frag.threat` | what were they afraid of | an unfinished evacuation shipyard | the seat lottery still draws a number; a projector shows how few seats there were |
| `frag.fold` | what did they do | an abandoned observatory, its telescope aimed at empty sky, the star catalogue struck out line by line | a planetarium that shows the old sky: the only sky full of stars in the game |
| `frag.reading` | what the instruments showed the first night | not settled | |
| `frag.silence` | why nobody undid it | the old council chamber under the Ministry's headquarters; the motion to undo it was tabled and never voted | a voting board with a lamp per delegation, all dark |
| `frag.switch` | can it be undone | the Gallery, an unfinished station in orbit round the Light (below) | the half-built undo, which the player finishes |

What is in the settled places, beyond the machine:

- **The shipyard.** Rows of hulls that never launched, which the player can salvage. A seat lottery pinned to the
  wall. A briefing room with the evacuation plan.
- **The observatory.** The telescope still points at a patch of sky with nothing in it; with power back the player can
  look for themselves. The catalogue's entries are struck out one at a time, in worse and worse handwriting.
- **The council chamber.** The Ministry built its headquarters on top, so the truth has been in its basement all
  along. Seats for five delegations, translators' booths with the last session still on tape, and minutes that stop
  mid-agenda.

## The Light

The black hole was never removed, only wrapped. An artificial star sits exactly where it is: the visible face of the
machine that holds it shut. In a night sky with almost nothing else in it, it is the one bright light, and all five
systems can see it. People steer by it and name children after it. It is the lid on the thing that will eat them.

- The player looks at the answer every night from the first hour without knowing it. Nobody alive finds the empty sky
  odd.
- `frag.threat` is where that turns: it is still there, and you have been looking at it.
- Each system keeps its own sun for daylight (assumed; day and night work as they do now). The Light is a thing of the
  night sky.
- Switching the machine off puts the Light out. That is the "artificial star you can turn off": one switch, not two.

## The Gallery

Where the story ends. A station out in space, never finished, dilapidated and beautiful, in orbit round the Light. It
can only be reached in a ship the player has built, so it is the endgame's own gate.

- **The gallery itself.** Rows of seats facing an enormous window, built so the dignitaries of the five systems could
  watch the fold succeed. They sat there and watched every star go out.
- **What was left.** Place cards on the seats, a speech still on the lectern, champagne never opened, and a
  commemorative film in the projector that was meant to be shown afterwards.
- **The undo.** The builders started making it and stopped. It is bolted on behind the gallery with the tools still
  laid out. The player finishes it (and builds the delay here, if they want one), then takes a seat.
- **Switching off, as seen from the seats.** The Light goes dark. A few seconds later the black hole's ring blooms in
  the same spot, and the nearest worlds begin to drift towards it. One window cannot show five systems: the Gallery
  shows the nearest go, and choosing where to stand (the delay) covers the rest.

## The colony (act one)

Settled: Rank and Suspicion are the colony's two numbers, and the Warden's offer is the one explicit choice between
the Trustee and the Escapee road. The beats' words in `Story/Storylets/Colony.json` are still draft.

The Trustee road goes on in `Story/Storylets/Ministry.json` (Karl, 2026-11-12: the Ministry builds up enough trust to
move the player out, then a story per rung; draft words). Inside the colony: the stores key, a head count one short
(a guard asleep), then the parole board lets the player out on licence, a last cup with Crumb, and the gate. Outside,
the Ministry speaks by memo and each rung waits for the faction's own tier (the licence it teaches): Clerk, Executive
Officer, Inspector of Works (whose first inspection is the colony and its warden), Under-Secretary and Crown Servant,
whose keys include a floor that is not on the plans: the council chamber of `frag.silence`. Not built: the ride out
of the gate and somewhere for the released player to live; the end says so in its `world` lines.

## Open

- Where `frag.reading` is found. An idea, not settled: a place about the forgetting, the Vigil's First Lamp Hall,
  whose founding flame was lit from the furnace that burnt the records. It needs the Vigil, and it would change what
  the fourth record answers.
- The cult. The pastry faith was removed on 2026-11-11. Karl's pick to add later is **the Vigil**: they keep lamps
  burning for lights that went out (small copies of the Light) and chant a litany of names nobody understands (the
  observatory's struck-out catalogue), and they disagree over whether the dark is at peace or the stars should come
  back. Add it as one faction file and its lines, nothing more, when the records are settled.
- Whether each system has its own sun (assumed yes, see the Light).
- Where the player comes from (left unexplained for now).
- In co-op, who may work the machine.

## Parked

Said once, not in the current story, not to be built until Karl brings them back: voter stations so the inhabitants
choose; using the antimatter bomb that a Crown Servant is given, and being hunted for it.

## How the story is changed

1. Karl says what should change, in words.
2. Claude edits the data: beats in `Story/Storylets/*.json`, the records in `Story/Fragments/Fragments.json`, what
   people say in `Story/Lines/*.json`.
3. `--check-story` must refuse nothing; then the story map is made again (`--story-map OUT.html`)
   and published to the same link.

How the engine works is [STORY.md](STORY.md); the data centres are [FRAGMENTS.md](FRAGMENTS.md).
