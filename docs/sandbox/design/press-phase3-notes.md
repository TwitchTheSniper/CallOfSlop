# press-sized-3 (phase 3: bay 2 and the working dies)
Worktree /home/claude/wt/partkit, branch feat/press-sized-3 from origin/main. Protocol 267.
## Done
## Next
- Pattern setting on press; bay 2 hopper; scale law; dies Button, Gear, Drive first
## Findings
- 13:xx code written (not built): round/pipe blanks, /1 rows removed, Pattern = recipe setting (die-local index), hopper (bay 2) feed/chute/F/card, Bay2Short, old bay-2 piece loosened, dies Button Gear Board Piston Cylinder Drive in generator
- verified: sweeps all 9 dies clean; presets 0 problems; Button card too little/enough + Pattern touch -> Switch; Drive kit + torque numbers (461/7200/5180); long pressed button pokes lamp in=1; timed (battery) press Stamping then output; host/client agree (Pattern CVT, hopper 2, output); save/load 2-process ok; old bay2 piece loosened
- next: merge origin/main, build, checks (gen-parts check, ccp, check-ports, gen-vehicles, sections), push
- DONE: checks clean, pushed final

# press-phase3b (2026-11-12, Windows PC; branch claude/press-phase3b-80fbdb; protocol unchanged, 267)
## Done
- Dies Shaft, Actuator, Spring, Cable, Control, Tank, Beam (PressParts.WorkingDies3b; die parts in the classes their bounds reach, lathe rows from Families). Bounds per design section 3.2 (Cable can't take rubber: not extrudable).
- Like bodies: pattern `"body": "like"`, `"along"` (PressDies, Pressed.BuildLike): the like part's boxes and sockets stretched to the blank, so joints, suspensions, shafts and reels keep working geometry. Blank bodies now move the like part's named ports onto the blank (Drive, Tank).
- Screens: 3-bay presses' bay cards 32 columns (h 0.5 cells), die screens 33 (5.8 x 1.2 cells).
- Client No power vs host Starved: a client that joined a running match never ran SandboxShareRules, so its CreativeRecycling stayed false; a creative press with no battery read No power. Now applied each client frame (GameSandbox.cs). No wire change.
- `panel` window-shot dump prints the creative flag and map.
## Verified
- sweeps of all 7 new dies: 0 problems, makes under 0.04 ms; --press-part numbers for piston, suspension, tank, reel.
- by hand (window-shot, throwaway saves): Shaft, Tank and Actuator dies pressed parts on a Small Press, kits taken; a client saw the pressed actuator by name; a battery-less press: host and client both Starved after the fix.
- --check-construct-parts, --gen-parts all --check (with LF copies: this checkout is CRLF, every generator reads "out of date" otherwise), sections Press stock, Sandbox inventory, Assembling, Display screens.
## Not verified
- a pressed Drive part driving a vehicle; a pressed actuator or spring moving in a built body; Large and Capital presses (cards only measured on a Medium).
## Next
- presets for the 3b dies, Auto by length as well as section (a 20 cm bar is always a drive shaft, never a stub axle by length), pipe shaft bore in torque, lamps and screens on like bodies.
