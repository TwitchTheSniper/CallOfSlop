---
name: story-writing
description: Write or change Call of Slop's story content - storylets (beats), lines people say, topics, lore items, fragments, plays and place states under Story/. Use when the user gives a note about the story in plain words, when a beat's words are still draft, or when adding a quest, a road or a person's dialogue.
---

# Writing story content

The user steers the story in plain words and doesn't edit the data: Claude writes it. `docs/story/STORYLINE.md` is the one written-down version of the story. Read it before anything else, every time; come to `docs/story/STORY.md` (100 KB: search it, don't read it whole) only for a file's format.

## 1. Settle what changes, in the storyline first

1. Find the part of STORYLINE.md the note touches. If the note changes the story (a place, a road, what a record answers, an ending), change STORYLINE.md first, in the same plain words, and move anything it replaces to "Open" or "Parked" rather than deleting an idea the user had.
2. If the note leaves a real choice open (who dies, which of two roads), write the version you would pick, mark its words as draft in the storyline, and say so in your report. One question at a time, and only when it holds up the work.

What the storyline already fixes, so no beat contradicts it: nobody from then is alive and there is no villain; the conflict is among the living, over what to do with what is dug up; the five records are found in any order; the Light is in the sky from the first hour and nobody alive finds the empty sky odd.

## 2. Write for the player in front of the screen

- **Show, don't tell.** A place answers "what were people doing here on the last day?" with things left lying, not with a speech. A person says what they want from the player now; what it means is left for the player to add up.
- **Don't overwhelm.** One new thing per beat. A beat's line is a sentence or two; a choice has two or three ends. If a beat needs a paragraph to explain itself, it is two beats.
- **About the person.** Each person has something of their own (an object, a habit, a grudge) that a line can lean on; `--talk-sim` shows what each already talks about. Last words are leads to a place or a record, never a summary.
- **The voice.** The Ministry speaks by memo, in instructions nobody could follow. Read a person's existing lines (`Story/Lines`) before writing new ones for them, so they still sound like themselves.
- Player-visible text is ASCII (no curly quotes, dashes or ellipsis characters), British spelling.

## 3. Where it goes

| What | Where | Format in STORY.md |
| --- | --- | --- |
| A beat that waits for the world to be ready, its choices and ends | `Story/Storylets/*.json` | "Storylets", "Paths" |
| What people say, by occasion | `Story/Lines/*.json` | "Lines" |
| What a person can be asked about | `Story/Topics/` | "Topics" |
| A thing lying about that opens a question with its owner | `Story/Lore/*.json` | "Lore" |
| The five records and their places | `Story/Fragments/Fragments.json` (ids never change) | docs/story/FRAGMENTS.md |
| Scenes between people, and a place's state | `Story/Plays/`, `Story/Places/` | "Plays", "Places" |
| A start, a duty, the tutorial's steps | `Story/Starts/`, `Story/Duties/`, `Story/Tutorial/` | docs/story/STARTS.md, DUTIES.md, docs/sandbox/TUTORIAL.md |

A storylet's qualities and facts are its memory: reuse the ones the road already has (Rank, Suspicion, a person's trust) before adding one.

## 4. Check it without playing, then in the world

```
dotnet bin/Release/net10.0/CallOfSlop.dll --check-story                    # must refuse nothing; read the storylets' warnings
dotnet bin/Release/net10.0/CallOfSlop.dll --storylet-sim [CAST] [--set Q=N,...] [--choose END] [--jobs]
dotnet bin/Release/net10.0/CallOfSlop.dll --talk-sim [CAST]                # what each person now talks about
```

- `--storylet-sim` plays a player through the storylets on the story's clock and says why each one that didn't play was held back: run it once per end of every choice you wrote, so no end is a dead end.
- A part or a job a beat hands to the player is tried through the real call (the tutorial once stalled on a part the player couldn't pick up: `.claude/rules/story-and-colony.md`).
- In a throwaway world (`--window-shot ... --map Glacier --survival`, or the Story window on F4 as host) start the beat and read it where the player will: on the radio, at the person, on the card. Too long on the card is too long.

## 5. Finish

1. Make the story map again and publish it to the same link: `--story-map OUT.html` (STORYLINE.md, "How the story is changed").
2. Mark in STORYLINE.md which words are settled and which are still draft, and date the "Last settled" line if the story itself changed.
3. Report in plain words what the player now meets, in order, and which choices you made for the user to overrule.
