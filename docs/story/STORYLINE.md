# The storyline as it stands

The one written-down version of the main story. Karl steers it in plain words; Claude writes the data. To read what is
in the game, open the story map (https://claude.ai/artifact/JJLq8Be3fd21fRvtdS22EF). When something here changes,
change it here and nowhere else. Last settled: 2026-11-11.

## The story

- **Then.** Something local was going to swallow the five systems. They banded together, each solving one part, and
  folded the space around the planets, the threat included. The fold did not stop: it ran out to infinity and took the
  rest of the universe. An accident of good intentions.
- **Between.** Nobody undid it. The records were withdrawn. Nobody from then is alive, and there is no villain.
- **Now.** The player crash-lands, wakes in the Ministry of Slop's penal colony, and uncovers the past from five
  abandoned places, one per system, in any order. The conflict is among the living, over what to do with what is dug up.
- **The end.** At the machine, with nobody there to argue with: switch it off (the universe returns, so does the
  threat, the planets fall); build a delay first (a short last round to see the people met, then it stops); or keep it
  (the five go on, everything else stays folded away).
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
| `frag.switch` | can it be undone | not settled (idea: a half-built reversal rig the player finishes) | |

A clue from minute one: the night sky has almost no stars, and nobody alive finds that odd.

## The colony (act one)

Settled: Rank and Suspicion are the colony's two numbers, and the Warden's offer is the one explicit choice between
the Trustee and the Escapee road. The beats' words in `Story/Storylets/Colony.json` are still draft.

## Open

- Where `frag.reading` and `frag.switch` are found.
- The cult. The pastry faith was removed on 2026-11-11. Karl's pick to add later is **the Vigil**: they keep lamps
  burning for lights that went out and chant a litany of names nobody understands (the observatory's struck-out
  catalogue), and they disagree over whether the dark is at peace or the stars should come back. Add it as one faction
  file and its lines, nothing more, when the records are settled.
- Where the player comes from (left unexplained for now).
- In co-op, who may work the machine.

## Parked

Said once, not in the current story, not to be built until Karl brings them back: voter stations so the inhabitants
choose; an artificial star that can also be switched off; climbing the Ministry's ranks to the antimatter bomb and
being hunted for using it.

## How the story is changed

1. Karl says what should change, in words.
2. Claude edits the data: beats in `Story/Storylets/*.json`, the records in `Story/Fragments/Fragments.json`, what
   people say in `Story/Lines/*.json`.
3. `--check-story` must refuse nothing; then the story map is made again (`python3 tools/story/story_map.py OUT.html`)
   and published to the same link.

How the engine works is [STORY.md](STORY.md); the data centres are [FRAGMENTS.md](FRAGMENTS.md).
