# Fragments and the data centres

Karl, 2026-11-10: the main storyline is uncovered in pieces. Long ago something was going to swallow the five systems; the
builders folded the space round the planets, the threat with it, and the fold rolled on to infinity and took the rest of the
universe. Five abandoned, encrypted data centres each hold one record of it, found in any order. This is step 1 of that plan:
what a fragment is, the Logbook, and the first data centre.

Code: `Source/Story/StoryFragments.cs` (the file and the terminal's screen, every peer), `Source/Game/GameStoryFragments.cs`
(the host: the sites, the decode, who holds what, the message), `Source/UI/UiLogbook.cs` and `Source/App/Program.Logbook.cs`
(the window). Data: `Story/Fragments/Fragments.json`, `Story/Relations/Fragments.json`, `Story/Verbs/Fragments.json`. The part:
`Data Centre Terminal` (`tools/constructs/office_parts.py`), the building: `Sealed Data Centre` (`tools/constructs/building_parts.py`).

## A fragment

- **In the file**: `id` (stable: `frag.threat`, `frag.fold`, `frag.reading`, `frag.silence`, `frag.switch`; the Thousand Layers'
  scripture and storylets name them), `title`, `kind` (the line about what the record is), `text` (paragraphs) and `hint` (what
  the Logbook says while it is missing). The words are the first draft from the storyline talk
  ([Fragments and scripture (draft)](https://claude.ai/artifact/N8XemUxW1d9rseLeDyTHxZ)); change them freely, keep the ids.
- **In the story world**: a Thing named by the id. A player who has it holds the relation `Fragment` towards it, its value the
  source's entity plus one (the site, or a person who told it; -1 nobody), and the `Fragments` quality counts them. A `Recover`
  event (actor, item the fragment, target the source) goes in the chronicle. So it is saved with the story section of the world
  file like anything else in the story, and a storylet can say `"?me Fragment 'frag.threat'"` or `"?me Fragments >= 3"`.
- **Any order**: nothing about a fragment depends on another. `Game.FragmentGive(player, index, source)` is the one way in; a
  data centre's terminal calls it, and later a person telling a story or a storylet's end can too.

## The Logbook (F1)

A Sandbox window (rebindable, binds version 25; Esc closes it): "Old records: 2 of 5 found", each held record with its text and
where it was found, a `?` line with the hint for each one still missing, and the player's leads (the story's `Lead` relation:
"Old Mine" from Crumb's tea, a data centre they walked into), each `?` until what it points to is found (a site whose `name` or
`lead` it is gave them its fragment). It reads only what the host sent.

## A data centre

- **Where**: each `"sites"` entry is a blueprint the host stands on level ground `from` to `to` metres from the penal colony's
  yard, when it builds the colony (with the barracks and the Remote Works) or when a world from before the sites loads. The
  Opening section of the world file keeps each site's id and construct after the depot's (an older reader stops before it).
- **Found**: within 30 m of it a player gets a `Lead` on the site (its Thing, named by `name`) and the `arrive` notice.
- **Power**: the Sealed Data Centre has no battery, so its terminal is dead. Fit a Battery (or any power plant) anywhere on the
  building's frame and the terminal's circuit is live (docs/sandbox/CONSTRUCTS.md "Power"); the `power` notice says so.
- **Decode by building**: powered, the terminal (a logic part, kind `cipher`: input port `key`, output `carrier`) puts out a
  carrier, a whole number from 1 to 9, a new one every `every` seconds, and its screen says what it wants back: `SEND CARRIER x 3
  + 4`. The player builds the sum out of logic parts on the building and wires carrier into it and its result into `key`: two
  Math Blocks (multiply with b 3, then add with b 4) do it. A carrier answered within its time is a lock; one that goes by
  unanswered starts the count again; `locks` in a row decode it. The carrier changes, so a number typed on a Numpad won't keep up.
- **Decoded** (the part's State bit 0, saved with the world): the screen says DECODED and the record's title, the carrier stops,
  and any player standing within 3 m of the powered terminal copies the fragment (`copied` notice). The circuit can come off.
- **The screen** reads the part on each peer: power from the circuit, the carrier from its signal, and the rest from its
  `Reading` (sent exactly): the locks so far (9: decoded) times 10000 plus the key heard, clamped to +-999.

| Site key | Means |
| --- | --- |
| `id`, `name`, `blueprint`, `fragment` | its stable id, what players call it, the blueprint, the fragment it holds (one site a fragment) |
| `lead` | a lead it answers ("Old Mine"): shown found once its fragment is held |
| `from`, `to` | metres from the colony's yard |
| `multiply`, `add`, `locks`, `every` | the sum (whole numbers, multiply not 0), carriers in a row, seconds a carrier |
| `source` | where the Logbook says it came from |
| `arrive`, `power`, `decoded`, `copied` | the notices |

## Network and save

- **`Msg.Fragments`** (protocol 228), server to one client, reliable, when it changes (checked once a second): u8 sites, u16 each
  site's construct (the terminals' screens find their site by it); u8 held, (u8 fragment, str source) each; u8 leads, (str name,
  u8 found) each.
- What a player holds is in the story (above); the sites' constructs in the Opening section; a terminal's decoded bit in its part.

## Not yet

The other four data centres (step 5 of the plan), fragments told by people or broadcast on the radio (step 4), the colony roads
that lead to the first one (step 3: Crumb's "Old Mine" lead already resolves when its fragment is found).
