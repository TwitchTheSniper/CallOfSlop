# Start-up time: build notes (phases 1-3)

Working notes for `claude/startup-phases123` (docs/sandbox/design/startup-time.md). The 2-core cloud box.

## Baseline (main 79c26b0f2b, this box)

- `--selftest-only "Keybinds"`: after a build 35.7 s, warm 13.9 s.
- Window to menu (`--no-splash`, xvfb), the maps line in the first lobby frame: after a build 13.7 s (65 built), warm
  1.8 s (65 cached).
- `--window-shot` (offline creative), whole run: Proving Ground 22.2 s after a build, 12.8 s warm; Glacier 15.4 s warm.
- Archived map hosted: "Zombies cathedral" section (Ashgrave Cathedral) 2.55 s inside a run that had built every map.
- Map sections before the change (`Maps, Map files, Map reload, Map packs, Planetoid, Mods, Save folder, Map bake,
  Baked lighting, Light flicker, Light leaks, Map contract, Big maps, Map select, Zombies cathedral`): 56.3 s, two
  failures that are on main already: "Baked lighting: the world block is mode 1 with 9 dynamic lights" and
  "Construct Yard: the "test" block wasn't read".
- `--bake DIR` from main: 65 maps, 65 nav graphs; kept in /tmp for the byte comparison (the payload, not the build id).

## Steps
