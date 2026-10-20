# Call of Slop

A tiny multiplayer arena shooter built directly on the [BRUTAL framework](https://github.com/ahwootech/BRUTAL).
Pick a loadout from 50 weapons and a map from 8 blocky prototype maps, then frag each other or a pack of bots.

| BRUTAL library | Used for |
| --- | --- |
| `Ahwoo.BRUTAL.Glfw` | Window, OpenGL context, keyboard and mouse |
| `Ahwoo.BRUTAL.OpenGl` | Rendering (OpenGL 4.1 core, everything is a box) |
| `Ahwoo.BRUTAL.RakNet` | UDP networking (listen-server + clients) |
| `Ahwoo.BRUTAL.ImGui` + `.Backends` | Lobby, menus, loadout screen, HUD text, scoreboard |
| `Ahwoo.BRUTAL.OpenAl` | 3D positional weapon audio |

## Requirements

- .NET 10 SDK
- Windows x64 or Linux x64, a GPU with OpenGL 4.1, and speakers or headphones
- Internet access the first time you build (NuGet restores the BRUTAL packages)

## Play

Double-click **`Start.bat`**. It builds the game if anything changed (the first build downloads the BRUTAL packages), then opens straight into the **lobby**:

| Lobby option | What it does |
| --- | --- |
| **Single player** | You against 1-7 bots (Easy, Normal or Hard). Fully offline, nothing touches the network. The game pauses while the Esc menu is open. |
| **Multiplayer: Host** | Starts a match others can join. It opens the port on your router automatically with **UPnP** (can be turned off), and you can fill empty slots with bots. Real players replace bots as they join. The address friends should use appears at the top-left of your screen. |
| **Multiplayer: Join** | Type the host's address and join. Esc cancels while connecting. |
| **Settings** | Four tabs. **Player:** name, sensitivity, invert Y, FOV, network stats. **Controls:** rebind every action to any key or mouse button. **Video:** windowed / borderless fullscreen / fullscreen, resolution, UI scale, HUD width, VSync. **Audio:** master volume. Changes apply immediately and are saved to `settings.txt`; the same screen is in the Esc menu during a match. |

In a match, **Esc** opens the menu (Resume, Change loadout, Settings, Leave match, Quit). Leaving takes you back to the lobby, and as host it ends the match and closes the router port.

To test on one PC: run `Start.bat` twice. Host in one window and join `127.0.0.1` in the other.

- The first time you host, Windows Firewall asks whether to allow Call of Slop. Click **Allow**.
- If the host's address says "LAN only - UPnP unavailable", turn on UPnP in your router's settings or forward UDP 60000. Same-network play works either way.
- "Behind a second NAT" means a second router or your ISP's carrier-grade NAT. Use a LAN or a virtual LAN tool like Tailscale or ZeroTier.
- The game has no console window; its log is written to `callofslop.log` next to the exe.

Command-line shortcuts (skip the lobby): `CallOfSlop.exe host [port]`, `CallOfSlop.exe join <ip> [port]`, plus `--no-upnp`, `--lag=ms`, `--loss=%` for testing.

### Matches

- **First to 11 kills wins, but you must be 2 kills clear** of the next player (like tennis). At 11-10 the match carries on until someone leads by two.
- **5-second countdown** at the start of every match: everyone is frozen (you can still look around), the loadout menu opens, and whatever you have selected deploys automatically at GO.
- **10 minutes max.** When the clock runs out, the kill leader wins; a tie at the top is a draw.
- The clock and the score (you vs. the best other player) sit at the top of the screen. It turns orange with "Win by 2!" when the match goes to deuce.
- At the end you get VICTORY / DEFEAT / DRAW and the final scoreboard. Nobody can be hurt on that screen, and a new match starts on the same map after 12 seconds.
- Same rules in single player and multiplayer; the host's game decides when a match ends.

### Attachments (gunsmith)

Every weapon has up to six attachment slots: **Optic, Muzzle, Barrel, Underbarrel, Magazine, Stock**. In the loadout menu, the **Primary** and **Secondary** tabs each have two sub-tabs: *Choose primary/secondary* and *<your selected gun> attachments*. The attachments sub-tab always shows the gun currently picked in that slot, with how many attachments are unlocked and what's equipped. Hover an attachment to see its pros, cons and the resulting stats.

- **Optics:** Red Dot, Holographic, 2.5x ACOG, 4x Scope, 8x Sniper Scope. Each has its own reticle and zoom, and the bigger the optic, the slower you aim.
- **Muzzle:** Suppressor (quiet shots, -15% range), Compensator, Muzzle Brake, Choke (shotguns).
- **Barrel:** Long, Short, Heavy. **Underbarrel:** Vertical Grip, Angled Grip, Laser.
- **Magazine:** Extended, Fast, Drum. **Stock:** Light, Heavy, No Stock.
- Which attachments fit depends on the weapon class (pistols take few, snipers take the 8x, shotguns get the Choke).
- **Unlocks are per weapon** with weapon XP (see Progression): 3-7 matches with a gun to unlock everything on it. Weapon XP is saved in `progress.txt`, and Reset level clears it.
- The server applies the same attachments to the same stats, so they're fully networked. Other players hear your suppressor and see your optic and grip.

### Loadouts

Open **LOADOUTS** from the main menu, or press **B** in a match. The screen lists your loadout slots on the left. Pick one to make it the loadout you spawn with; it's marked with an orange star. The selected loadout is shown on the right as cards: **Primary Weapon**, **Secondary Weapon**, **Perks**, **Lethal** and **Tactical**. Click a card to change that part, and **Rename** to name the loadout. Each loadout keeps its own attachments, and a gun you move into another loadout starts with the build you last used on it. Everything saves to `loadout.txt`. An older single-loadout file becomes slot 1.

- **More slots unlock as you level up.** Level 1 gets one slot, and the tenth arrives at max level, evenly spaced: levels 1, 6, 11, 17, 22, 27, 32, 38, 43 and 48. Locked slots show the level they unlock at.
- **Your progression lives on the second tab** of this screen: level, XP, stats and every unlock by level.

### Perks

Each loadout carries one perk from each of three categories. Every perk only works in one situation, but it's strong when that situation comes up. Perks light up on the HUD (under your health) while they're active, and a banner announces the big moments.

| Category | Perk | When | What it does |
| --- | --- | --- | --- |
| Movement | **Low Profile** (start) | Crouched or prone | Move 80% faster; Recon Pulses can't reveal you while you're low |
| Movement | **Head Start** (level 3) | Just spawned | 35% faster for the first 4 s of every life |
| Movement | **Parkour** (19) | Walls, windows, roofs | Reach ledges 0.6 m higher, climb twice as fast, jump 25% higher |
| Movement | **Afterburner** (36) | Just got a kill | 35% faster for 4 s after each kill |
| Survival | **Flak Jacket** (start) | Grenades | 75% less explosive damage, including your own |
| Survival | **Bloodthirst** (9) | Just got a kill | Each kill heals you to full |
| Survival | **Entrenched** (25) | Crouched/prone and still | 40% less bullet damage |
| Survival | **Last Stand** (41) | Once per life | The first lethal hit leaves you on 1 HP and immune for 1.5 s |
| Damage | **Point Blank** (start) | Within 8 m | Bullets and melee hit 40% harder |
| Damage | **Opener** (14) | Enemy on full health | Bullets and melee hit 50% harder |
| Damage | **Deadeye** (31) | Beyond 30 m | Bullets hit 40% harder |
| Damage | **Flanker** (47) | Hitting their back | Bullets and melee hit 60% harder |

Movement perks run inside the shared movement code (client prediction and the server agree). The server applies survival and damage perks, and bots carry random perks.

### Melee

Press **V** for a quick rifle-butt. It reaches about 2.4 m and does 55 damage, so two hits kill. A hit **from behind always kills**. It cancels a reload, has a 0.8 s cooldown, and you can't fire for a moment after swinging. It's resolved on the server with the same lag compensation as shots. Bots use it when you get right up close.

### Respawning

- **Loadouts only change between lives.** During a life you can still edit loadouts, and the changes apply the next time you spawn. The one exception is the 5-second pre-match countdown, when changes apply immediately.
- **Nobody is force-respawned.** When you die, the loadout screen opens after a moment. Pick a loadout, then click **RESPAWN**. The button counts down the 3-second respawn delay, and clicking early queues it. You also start every session on this screen.
- Close the screen to just watch. Press the loadout key to reopen it.

### Mantling

Hold **forward + jump** at a ledge up to 1.7 m high (crates, low walls, window sills) to climb onto it. Windows are 1.3 m tall, so you go through them in a crouch: you land on the sill and carry on through. Keep holding forward to drop out the other side. Mantling is part of the shared movement code, so it's predicted and smooth online too.

### Server browser and public games

**Multiplayer** has three tabs:

- **Server browser:** lists games to join with name, map, players (and bots) and ping. Click Join. Games on your network show up automatically: the browser broadcasts on the local network and every hosted game answers. Public internet games show up when a master server is set (see below). The list refreshes every few seconds.
- **Host a game:** map (random by default, or *Select map* to pick from the full list), port, UPnP, bots, **server name**, and **Advertise server**, which lists the game publicly through the master server.
- **Join by address:** type an IP and port directly.

**About public listing:** a public list needs something always online that hosts can report to and browsers can ask. That's the **master server**, built into the game: run `CallOfSlop.exe --master` (optionally followed by a port; the default is UDP 27050) on any always-on PC with that UDP port reachable. It tries UPnP itself. Then everyone enters that PC's address under **Settings > Player > Master server**. The master only stores addresses: names, maps, players and pings come straight from each game. Without a master server, the browser still finds every game on your LAN.

Single player also defaults to **Random map**, with *Select map* to choose. With random picked, the lobby background tours the maps.

### Map vote

At the end of every match the results screen shows **three maps to vote on**: one small, one medium and one large, never the map you just played. Click one or press **1 / 2 / 3**. You can change your vote until the 12-second timer runs out, and the live tally shows which map is leading. Bots vote too, leaning towards smaller maps when there are few players. The next match starts on the map with the most votes, with ties broken at random. Players who join during the results screen see the ballot and can vote.

### Maps

All maps share the same primitive style: everything is a box, with the grey-and-orange dev grid. Buildings are walk-in, with doors, windows to shoot through, internal stairs between floors and, on many, a staircase onto the roof. Pick the map in the lobby (Single player or Host); the background previews your choice.

| Map | Size | What it is |
| --- | --- | --- |
| Proto Box | 40 x 40 m | The original dev box with nine crates. |
| Courtyard | 64 x 64 m | A plaza with a fountain, ringed by four two-storey houses with roof access. |
| Crossroads | 100 x 100 m, plus-shaped | A town on two crossing streets: rows of enterable houses, cars, barricades, dead-end alleys. |
| Warehouse 9 | 56 x 36 m, indoor | A container maze, open containers you can run through, catwalks along both walls, an office. |
| Long Shot | 140 x 30 m | A sniper canyon with bunkers at both ends, a raised central mesa and boulders. |
| Ziggurat | 70 x 70 m | A three-tier stepped pyramid with stairs on every face and a shrine on top, ringed by obelisks. |
| Scrapyard | 44 x 36 m, L-shaped | A tiny desert junkyard: car stacks, container tunnels and a two-storey shack. |
| Rooftops | 60 x 60 m | Four tall buildings, plank bridges between the roofs and a sniper perch. Fight high or low. |
| **Medium** | | |
| Harbor | 110 x 72 m | Raised quay with gantry cranes, container rows, a harbour office and a long warehouse. |
| Market Street | 120 x 60 m | A high street of two-storey shops, market stalls down the middle, alleys behind. |
| Quarry | 100 x 100 m | An open pit: terraced rims, a central mound, a conveyor gantry, rock piles. |
| Trenches | 100 x 84 m | Two zig-zag trench lines (mantle over the berms), bunkers with firing slits, craters. |
| Train Yard | 120 x 64 m | Five tracks of boxcars and open gondolas, a station platform, a footbridge perch. |
| Office Park | 104 x 104 m | Four three-storey office blocks around a courtyard, parking lots of cars. |
| Ruins | 96 x 96 m | A stepped temple with a pillared hall, broken colonnades, crumbling walled courtyards. |
| Overpass | 112 x 80 m | An elevated highway deck over a junction: fight on top or among the pillars below. |
| **Large** | | |
| Farmland | 200 x 200 m | Hedgerow fields, a farmstead with barns and silos, an orchard, a windmill hill. |
| Downtown | 180 x 180 m | A city grid: tall towers, walk-in shops and apartments, a central plaza, traffic. |
| Canyon | 240 x 90 m | A desert canyon with sheer walls, rock shelves, two high bridges and mining camps. |
| Airfield | 220 x 160 m | A runway, three hangars with planes, a control tower, terminal and fuel farm. |
| Village | 180 x 180 m | Houses on winding lanes, a church with a bell tower, stone walls, woods around the edge. |
| Industrial | 200 x 160 m | Factory halls, smokestacks, a pipe-rack catwalk across the site, container yard, tank farm. |
| Highlands | 220 x 220 m | Big terraced hills with outposts on top, pine woods and boulder fields. |
| Military Base | 200 x 200 m | A walled compound with corner watchtowers, barracks, a vehicle depot, helipad and HQ. |

The lobby groups maps by size. Medium and large maps add distance haze so the far side fades into the sky. Bots on big maps pick waypoints they can walk to in a straight line.

**Maps are data files.** Each map is one `.json` file in the `Maps` folder: its name, size, sky colour and a list of building ops (`Building`, `WallX`/`WallZ` with door and window holes, `Slab` with stairwells, `StairsX`/`StairsZ`, `Container`, `Car`, `Hill`, `Tree`, `Watchtower`, `Hangar`, `Bridge`, `TrainCar`...). To add a map, drop a file (or a folder of them) into `Maps` and click **Refresh maps** in the lobby; no restart or recompiling. In single player, **Reload map files** in the Esc menu rebuilds the map you're standing in from its file, for quick edit-and-look map making. Anyone can make one: **[Maps/README.md](Maps/README.md)** is the map makers' guide, with a full example and every op. A file with a mistake is skipped with a message that names the file and the problem (also shown in the lobby), and `CallOfSlop.exe --check-map FILE` checks one without starting the game. Online, servers and clients identify maps by name plus a geometry fingerprint, so a player missing a host's custom map is told which file to get, and the server browser shows maps you don't have in orange.

Movement handles any stack of boxes: you stand on anything, walk up steps up to 0.55 m, and bump your head on ceilings.


Surfaces are textured with a few tiny patterns generated in code (concrete, brick, wood planks, metal panels, floor tiles, dirt), tinted by each block's colour. The material is picked automatically from each block's shape and colour: crates get planks, walls get brick or concrete, floors and roofs get tiles, sandy ground gets dirt. Proto Box keeps its prototype grid.
### Controls

These are the defaults; everything except Esc and the mouse wheel can be rebound in **Settings > Controls**. Click a binding, then press the new key or mouse button. If that input is already used, the two actions swap.

| Input | Action |
| --- | --- |
| WASD | Move |
| Mouse | Look |
| Left mouse | Fire (semi, burst or full auto depending on the gun) |
| Right mouse (hold) | Aim down sights (zoom, tighter spread, slower movement; snipers get a scope) |
| R | Reload |
| 1 / 2, Q, mouse wheel | Switch between primary and secondary |
| Space | Jump (if you're crouched or prone, it stands you up first) |
| Left Shift | Sprint (stands you up if you're crouched or prone) |
| C / hold Left Ctrl | Crouch (C toggles, Ctrl holds) |
| Z | Go prone (toggle) |
| G | Throw grenade |
| F | Use equipment (Recon Pulse) |
| V | Melee |
| B | Loadout menu |
| Tab (hold) | Scoreboard |
| Esc | Close the menu / release the mouse (click to recapture) |

### Display

- **Windowed:** pick a size from your monitor's resolutions, or drag the window edges. The game remembers the size and position.
- **Borderless fullscreen:** a frameless window covering the monitor at its native resolution. Alt-tab is instant.
- **Fullscreen:** exclusive fullscreen at the resolution you choose.
- **UI scale:** menus and HUD scale with the window height automatically (about 1.2x at 1080p, 1.6x at 1440p). The slider adjusts on top of that, from 0.6x to 2x, and applies when you let go.
- **HUD width:** on ultra-wide monitors the in-match HUD (session info, kill feed, health, ammo, match bar, banners, scoreboard) stays inside a centred area so it's easy to read. **16:9** (default) keeps it 2560 px wide on a 3440x1440 or 5120x1440 screen; **21:9** allows 3360 px; **Full width** uses the screen edges. On 16:9 or narrower screens all three are the same. The crosshair is always centred, and the scope and damage effects always cover the whole screen.

### Stances

| Stance | Height | Speed | Spread | Recoil | Notes |
| --- | --- | --- | --- | --- | --- |
| Standing | 1.8 m | 100% | 100% | 100% | Can jump and sprint |
| Crouched | 1.2 m | 50% | 75% | 85% | Fits behind low walls and crates |
| Prone | 0.6 m | 22% | 50% | 60% | Can't go prone in mid-air. Your hitbox is long and low, and your head is out front |

You can't stand up where there's no headroom (for example under a container roof); from prone you'll get up to a crouch if that's all that fits. Stance is part of the shared 60 Hz simulation, so prediction, the server and lag compensation (which rewinds stance as well as position) all agree. Bots crouch for long-range shots; Hard snipers go prone, and Hard bots crouch-peek in close fights.

### Progression

You earn XP in **single player and multiplayer** alike. There are two tracks, both filled by the same play:

- **Account XP (levels):** +100 per kill, +50 more for a headshot, +25 more for a grenade kill, **+150 for finishing a match** and **+250 for winning it**. **Each level unlocks a weapon, grenade or equipment**, 48 levels in all. Perks and loadout slots unlock on top of that, spread evenly over the same levels. A level costs ~2,400-3,800 XP.
- **Weapon XP (attachments):** each gun earns its own XP: +100 per kill with it, +50 for a headshot, plus +150 per match for the primary you carried (+75 for the secondary). Its attachments unlock along a curve, with the first few quickly and the last ones slower.

The pacing is a rough target rather than a hard rule. A typical match is around 7 kills, which is ~1,000 XP on each track. So:

- **A gun is fully kitted out in 3-7 matches.** Guns with few attachment options (pistols) are on the short end, and guns with lots (rifles, DMRs) on the long end.
- **A new level, and with it a new weapon, comes at about the point your current gun is ~60% kitted.** That works out to roughly every 2-3 matches, so you get time with each gun before the next one shows up.

The lobby shows your level and next unlock, and the **Progression** tab of the loadout screen lists every level. In a match, XP pops up by the crosshair, and banners announce level-ups and new attachments. Progress is saved to `progress.txt`. Older saves carry over: old per-weapon kill counts become weapon XP.

- **Level 1:** BR-1 Prototype, Vanguard AR, Hornet SMG, Breacher Pump, Marksman M14, P9 Sidearm, Mk.23 Tactical.
- **Levels 2-48:** the other 43 weapons plus the Frag Grenade (level 4), Recon Pulse (8), Impact Grenade (13) and Smoke Grenade (20), in a fixed order. The fun ones come last: Grinder Rotary, Railspike, Toy Blaster.
- Locked items appear as `[LOCKED] ... level N` (weapons) or `[LOCKED] ... N weapon XP` (attachments). Bots ignore progression.
- Unlocks are kept on each player's own PC.

### Grenades and equipment

The loadout has a **Grenade** slot (2 per life) and an **Equipment** slot (1 per life), both refilled when you respawn. Projectiles are simulated by the server: they bounce off walls and floors, and blasts are blocked by walls.

| Item | Unlock | What it does |
| --- | --- | --- |
| Frag Grenade | Level 3 | 2.2 s fuse, bounces. Up to 150 damage, kills within about 3 m, hurts out to 7 m. Your own frag hurts you at half damage, and going prone cuts blast damage by 30%. |
| Impact Grenade | Level 8 | Explodes on first contact. Up to 110 damage within 4.5 m. |
| Smoke Grenade | Level 13 | Pops a 5 m cloud for 14 s. Blocks vision, and bots can't see through it. |
| **Recon Pulse** (equipment) | Level 5 | Throw it anywhere; it sticks where it lands. For 12 s, every enemy within 18 m of it shows to you as a red silhouette through walls. It beeps once a second, so enemies can hear where it is. |

Bots on Normal and Hard throw frags (and sometimes impacts) at targets 6-15 m away, using a proper ballistic arc. Explosions shake your screen when they're close.

### Loadouts and weapons

The **loadout menu** opens when a match starts (and any time with **B**, or from the Esc menu). Pick a **primary** and a **secondary** from **50 weapons**. Hover a weapon to compare damage, fire rate, DPS, range, accuracy, recoil, mobility, magazine, reload and draw time, see shots-to-kill and damage at range, and use **Test fire / Test reload / Test equip** to hear it. Click **Deploy**. If you're out of combat (no shots fired or taken for 4 s), the change applies instantly; otherwise it applies when you respawn. Your loadout is saved to `loadout.txt` next to the game.

| Class | Weapons |
| --- | --- |
| Pistols (secondary) (9) | P9 Sidearm, Mk.23 Tactical, Viper .45, Rattler Revolver, Bastion Hand Cannon, Wasp Machine Pistol, Stinger Burst Pistol, Hush SD, Pocket Derringer |
| SMGs (7) | Hornet SMG, Buzzsaw, Courier, Whisper SD, Kestrel PDW, Tempest, Ratchet Burst SMG |
| Assault rifles (8) | BR-1 Prototype, Vanguard AR, Sentinel, Raptor, Bulldog, Tri-Shot, Phalanx Bullpup, Specter SD |
| Marksman rifles (6) | Marksman M14, Longbow DMR, Reaper Battle Rifle, Talon Burst DMR, Hawk Semi, Warden Lever-Action |
| Snipers (5) | Needle Bolt-Action, Leviathan .50, Kite Semi-Sniper, Ghost SD, Railspike |
| Shotguns (6) | Breacher Pump, Thunder Auto, Double Barrel, Slug Pump, Street Sweeper, Scattergun Lever |
| LMGs (5) | Anvil LMG, Hailstorm, Bastille, Grinder Rotary, Torrent |
| Special (4) | Nailgun, Arc Carbine, Old Faithful Musket, Toy Blaster |

Every weapon has its own stats: damage and headshot multiplier, pellets, fire rate and mode (semi/burst/auto), magazine, reload, hip and aimed spread, recoil, damage falloff, movement speed, zoom and draw time. All tuning lives in `Weapons.cs`.
The server checks everything: you can only fire weapons you own, at their real fire rate and spread, and you can't move faster than your fastest weapon allows.

### Sound

Every weapon has its own **gunshot, reload and equip sound**, synthesised when the game starts from the weapon's stats: calibre gives depth and thump, there's crack and echo, suppressed guns go *thwip*, and energy, pneumatic and toy guns get their own recipes. Reloads follow the weapon type (mag swap, shell-by-shell plus pump, bolt, belt box). There are also hit, headshot, kill, hurt, death and respawn cues. Other players' shots and reloads are positioned in **3D** with OpenAL, so you can hear where people are.

## Development and version control

The repository holds only the source: `Source/`, `Maps/`, `tools/`, the project file and the docs. Build output (`bin/`, `obj/`) is ignored. So are your personal game files (`progress.txt`, `settings.txt`, `loadout.txt` and logs), which live next to the built game in `bin\Release\net10.0\` and are never committed.

- **Get started:** clone the repo, then run `Start.bat` (or `dotnet build -c Release`). NuGet fetches the BRUTAL packages on the first build.
- **Automatic checks:** with `.github/workflows/build.yml` in the repo, every push runs **Build and self-test** on GitHub Actions. It builds on Windows, runs `CallOfSlop.exe --selftest` and attaches the logs to the run. A red X on a commit means it didn't build or a check failed; open the run to see which.
- **Line endings:** `.gitattributes` keeps text files LF in the repo and batch files CRLF, so diffs stay clean whichever editor or OS touched a file.

## Testing without playing

- `tools\BuildCheck.bat` builds the game, then runs the automatic checks and writes `bin\buildcheck.done` when finished (the build output is in `bin\build.log`). The window smoke test only runs if a file called `tools\run_smoke.flag` exists.
  - **`CallOfSlop.exe --selftest`** runs headless (no window). It checks every map: spawns, waypoints, and a 90-second offline match with 7 bots looking for crashes, stuck bots and slow ticks. It also round-trips every map through the map file format, checks that the example in the map guide loads and that broken files give useful errors, checks mantling through a window and onto a crate, every attachment on every weapon, and snapshot packet sizes. Results go to `bin\Release\net10.0\selftest.log`.
  - **`CallOfSlop.exe --smoketest`** opens the real window and steps through the lobby, the loadout and gunsmith screens, the respawn flow and a few seconds of play on six maps. It logs fps and GL errors to `callofslop.log`, then quits. It never saves your settings or progress.

## How it works

**Netcode: prediction, interpolation and lag compensation**

Both sides run the same fixed **60 Hz** simulation (`World.Simulate`), independent of frame rate. Rendering blends between ticks, so motion is smooth at any FPS.

| Problem | What the game does |
| --- | --- |
| Your own movement lagging behind your keys | **Client-side prediction.** Your input is simulated on your PC immediately, every tick. |
| Server disagreeing with your prediction | **Reconciliation.** Each snapshot tells you the last input the server ran. Your client resets to the server's state, replays the inputs the server hasn't seen yet, and fades any leftover error out over about 100 ms instead of snapping. |
| Other players jittering or teleporting | **Entity interpolation.** Remote players are drawn a little in the past (about 70 to 300 ms, adapting to measured network jitter), smoothly between buffered snapshots. If packets drop, they're briefly extrapolated along their last velocity. |
| Having to lead targets by your ping | **Lag compensation.** The server keeps 2 s of position history per player and rewinds targets to the exact tick you were seeing when you fired (up to 400 ms back). If it looked like a hit, it is a hit. |
| Clients stuttering on the host's screen | **Server jitter buffer.** The server holds about 2 ticks of each client's input and consumes one per tick. If a packet is late, it assumes you're still holding the same keys, and the late packet is dropped when it arrives. |
| Packet loss | Every input packet repeats your last 4 commands, so a lost packet costs nothing. |

Movement is now server-authoritative, so clients can't teleport. Shots are still resolved only on the server.
Hit markers appear instantly from your own prediction, and the server confirms them.

**Testing bad connections on one PC:** add `--lag=150` (extra milliseconds on everything that side sends, plus some jitter) and/or `--loss=5` (percent of packets dropped):

```sh
Start.bat --lag=150 --loss=3
```

On clients, the window title shows ping, the current interpolation delay, and the size of the last prediction correction ("fix ... cm").

**Project layout**

```
CallOfSlop.csproj   Start.bat   README.md   NOTICE.txt
Maps/                map files (*.json) and the map makers' guide - copied next to the game on build
tools/               BuildCheck.bat (build + self-test)
Source/
  App/               Program (window, input, camera, scene + HUD drawing, smoke test), Settings, Keybinds, SelfTest
  Game/              World (physics, collision), Game (server/client logic, hitscan), Match, MapVote, Bots,
                     Throwables, Progression, ServerAdvert, Loadouts (slots + loadout.txt), Perks, Melee
  Maps/              Map (geometry + building ops), MapKit (bigger ready-made pieces), MapFile (JSON load/save)
  Net/               Net (RakNet wrapper, packets), ServerBrowser (+ master server), Upnp
  Weapons/           Weapons (catalogue + stats), Attachments, GunModel (procedural weapon models)
  Render/            Renderer (shader, cube mesh, boxes), Textures (procedural texture array)
  UI/                Lobby (menus, server browser, settings), Ui (HUD, scoreboard), UiLoadout (loadout screen), UiGunsmith
  Audio/             Audio (OpenAL playback + procedural sound synthesis)
```

**Command-line options:** `--selftest`, `--smoketest`, `--lag=MS`, `--loss=PCT`, `--no-upnp`, `--master` (run a public server list), and for map makers `--check-map FILE`, `--map-ops` (list every op and its parameters) and `--export-maps DIR` (rewrite every loaded map in the standard layout).

All tuning values (damage, fire rate, speeds, arena size) live at the top of `World.cs`.

## Licence note

BRUTAL is free to use, but section 2 of the BRUTAL License asks shipped products to credit BRUTAL, show the BRUTAL logo before gameplay and ship `NOTICE.txt`.
This prototype prints the credit and ships `NOTICE.txt`; add the logo splash before you distribute it.
