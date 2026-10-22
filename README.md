# Call of Slop

A tiny multiplayer arena shooter built directly on the [BRUTAL framework](https://github.com/ahwootech/BRUTAL).
Pick a loadout from 50 weapons and a map from 8 blocky prototype maps, then frag each other or a pack of bots.

| BRUTAL library | Used for |
| --- | --- |
| `Ahwoo.BRUTAL.Glfw` | Window, OpenGL context, keyboard and mouse |
| `Ahwoo.BRUTAL.OpenGl` | Rendering (OpenGL 4.1 core, everything is a box) |
| `Ahwoo.BRUTAL.RakNet` | UDP networking (listen-server + clients) |
| `Ahwoo.BRUTAL.ImGui` + `.Backends` | Lobby, menus, loadout screen, HUD text, scoreboard |
| `Ahwoo.BRUTAL.OpenAl` | 3D positional weapon audio, streamed procedural music |

## Requirements

- .NET 10 SDK
- Windows x64 or Linux x64, a GPU with OpenGL 4.1, and speakers or headphones
- Internet access the first time you build (NuGet restores the BRUTAL packages)

## Play

Double-click **`Start.bat`**. It builds the game if anything changed (the first build downloads the BRUTAL packages), then shows the BRUTAL logo for a few seconds (the BRUTAL licence asks for it before gameplay; click or press a key to skip it) and opens the **lobby**:

| Lobby option | What it does |
| --- | --- |
| **Single player** | You against up to 31 bots (as many as the map takes, see [Lobby size](#lobby-size); Easy, Normal or Hard), or **No bots (test mode)** with the slider at 0. Fully offline, nothing touches the network. The game pauses while the Esc menu is open. |
| **Multiplayer: Host** | Starts a match others can join. It opens the port on your router automatically with **UPnP** (can be turned off), and you can fill empty slots with bots. Real players replace bots as they join. The address friends should use appears at the top-left of your screen. |
| **Multiplayer: Join** | Type the host's address and join. Esc cancels while connecting. |
| **Settings** | Four tabs. **Player:** name, sensitivity, invert Y, FOV, network stats. **Controls:** rebind every action to any key or mouse button. **Video:** windowed / borderless fullscreen / fullscreen, resolution, UI scale, HUD width, name tags, head bob, VSync. **Audio:** master, music, effects and footstep volume, music style, kill frenzy music. Changes apply immediately and are saved to `settings.txt`; the same screen is in the Esc menu during a match. |

In a match, **Esc** opens the menu (Resume, Change loadout, **Kill yourself**, Settings, Leave match, Quit). Leaving takes you back to the lobby, and as host it ends the match and closes the router port.

- **Kill yourself (respawn)** closes the menu and kills you through the normal server path, then the usual death flow follows (death cam, then the loadout screen; in Gun Game you respawn on your own). It counts as a **suicide**: a death for you, no kill for anyone, your kill streak resets, and it never scores for a team or changes your Gun Game weapon. It's greyed out while you're dead or the match isn't live (warmup, countdown, results), and the server accepts it at most once a second. Online, clients ask the server, which decides.
- **No bots (test mode):** set the bot slider to 0 in Single player (or *Fill with bots* to 0 when hosting) to try weapons, attachments, perks and mode objectives on your own. Deploy (or Ready) starts the match straight away. While you're the only one in the match **the match clock stops and nobody can win**: team scores (hill points, flag captures) still count but stop one short of the limit, so the match never ends on its own. In team modes you're simply on a team by yourself. As soon as another person joins a hosted game, the normal rules apply again. The bot count is saved in `settings.txt` as before.

To test on one PC: run `Start.bat` twice. Host in one window and join `127.0.0.1` in the other.

- The first time you host, Windows Firewall asks whether to allow Call of Slop. Click **Allow**.
- If the host's address says "LAN only - UPnP unavailable", turn on UPnP in your router's settings or forward UDP 60000. Same-network play works either way.
- "Behind a second NAT" means a second router or your ISP's carrier-grade NAT. Use a LAN or a virtual LAN tool like Tailscale or ZeroTier.
- The game has no console window; its log is written to `callofslop.log` next to the exe.
- Text boxes take plain text only: pasting an image or a file does nothing (the log says `[ui] paste ignored: the clipboard has no text`), and pasted text is cut to printable ASCII. Names, server names and other text from other players are cleaned the same way, and a broken or hostile network packet is dropped with a `[net] dropped a bad ... packet` log line instead of closing the game.

Command-line shortcuts (skip the lobby): `CallOfSlop.exe host [port]`, `CallOfSlop.exe join <ip> [port]`, plus `--no-upnp`, `--lag=ms`, `--loss=%` for testing.

### Matches

- These are the **Free-for-all** rules (the default). The host can pick another game mode; see *Game modes*.
- **First to 11 kills wins, but you must be 2 kills clear** of the next player (like tennis). At 11-10 the match carries on until someone leads by two.
- **Score limit: Auto (default) or Fixed** (Single player and Host screens, under the game mode). Auto scales the limit with the number of players (people + bots) so a 24-player match takes about as long as an 8-player one: 11 kills is the limit at 8 players, more above that, fewer below (never under 7). It's set from everyone present when the match starts and only ever goes **up** if people join mid-match (never down when they leave). Fixed keeps the classic limits (11 kills, 40 team kills, 150 points, 3 captures). The match bar always shows the current limit. Formulas and measurements: [docs/SCORE-SCALING.md](docs/SCORE-SCALING.md). Saved in `settings.txt` as `score_limit`.
- **Warmup and ready-up** before every match (the first one and each one after the map vote): see *Ready-up* below.
- **5-second countdown** once everyone is ready: everyone is frozen (you can still look around), the loadout menu opens, and whatever you have selected deploys automatically at GO.
- **10 minutes max.** When the clock runs out, the kill leader wins; a tie at the top is a draw.
- The clock and the score (you vs. the best other player) sit at the top of the screen. It turns orange with "Win by 2!" when the match goes to deuce.
- At the end you get VICTORY / DEFEAT / DRAW and the final scoreboard. Nobody can be hurt on that screen, and a new match starts on the same map after 12 seconds.
- Same rules in single player and multiplayer; the host's game decides when a match ends.

### Ready-up

- Every match opens with a **warmup**. The loadout screen opens; you can pick a loadout, respawn and walk around, but nobody can be hurt or shoot. A panel on the left lists everyone with a tick for who's ready.
- Press **F3** (rebindable: *Ready up*) or click **READY UP** in the loadout screen footer or the panel. The 5-second countdown starts as soon as **every person** is ready. Bots are always ready.
- **Single player:** Ready, Deploy or Respawn starts the countdown straight away.
- **Hosting:** the panel has a **FORCE START** button. The Host screen also has *Auto-start the match once someone is ready* (off by default): when it's on, the match starts that many seconds after the first person readies up, even if others haven't.
- When the countdown starts, everyone is put back at a spawn point. People who join during the warmup start not ready. People who join mid-match, or who leave, never hold anyone up.

### Game modes

Pick the mode on the Single player or Host screen. It stays the same from match to match (the map vote only changes the map), and the server browser, the match bar and the results screen show it. Every mode but Zombies has the same 10-minute limit.

| Mode | Rules |
| --- | --- |
| **Free-for-all** | First to 11 kills (at 8 players; see *Score limit* above), win by 2. |
| **Team Deathmatch** | Two teams (Blue and Red), pick your side or let the game balance them (see *Picking teams* below), bots included. First team to **40 kills** at 8 players; with the Auto score limit it grows with the player count (70 at 16, 120 at 32). No friendly fire, grenades and streaks included (your own grenade can still hurt you). You spawn on your team's side of the map. |
| **King of the Hill** | Two teams. A glowing ring marks the hill. Hold it with **only your team** inside for **1 point a second**; while both teams are in it (contested) nobody scores. The hill moves to the next zone every **60 s**. First to **150 points**. |
| **Capture the Flag** | Two teams, a flag at each base. Touch the enemy flag to take it; bring it to **your own flag** to capture, which only works while your flag is at home. A carrier who dies drops it; a teammate touching a dropped flag sends it home, otherwise it returns by itself after **20 s**. Carriers keep their guns, and a light beam over them (and over a dropped flag) shows through walls. First to **3 captures**. |
| **Gun Game** | Everyone starts with the same gun, the first one in the unlock order, whatever they've unlocked. Each kill moves you to the next gun; a **melee kill also sets the victim back one gun**. The first kill with the last gun wins (a melee kill on the last gun doesn't count). Loadouts, grenades, perks and kill streaks are off. The ladder is 20 guns, evenly spaced through the 50-odd weapons in unlock order. |
| **Zombies** | Co-op for 1-4 against endless rounds of zombies (World at War style), only on maps with a Zombies layout (today the dev map **Zombies Test**). No time limit: the run ends when everyone is down. See "Zombies" below. |

Team modes colour everyone blue or red (players, scoreboard, kill card). King of the Hill is played by teams. The HUD shows the hill's holder, its distance and direction and when it moves; the state of both flags; or your Gun Game weapon, the next one and the leader. The scoreboard groups players by team (or by gun level). If a player leaves and the teams end up two apart, a bot switches sides.

On dev-pack maps (Zombies Test) the mode list also offers **Last Man Standing** (one life each, the last one standing wins), the Dev-only template that shows how a mode is written. For developers: every mode is a module under `Source/Game/Modes/<Mode>/` (rules flags, lifecycle, bots, HUD and replicated state in one class), registered in `GameModeRegistry`; [Source/Game/Modes/README.md](Source/Game/Modes/README.md) explains how to add one.

**Picking teams (play with your friends).** In every team mode you can choose **Join Blue**, **Join Red** or **Auto**:
- **Where:** the **TEAMS** window beside the loadout screen (it opens by itself in the warmup; the **TEAMS** button in the loadout footer opens or hides it, also on the respawn screen and when you join a match in progress) and **Esc > TEAMS**. Both show each team's roster, so you can see where your friends are; **Join** next to a person on the other side puts you on their team. A `*` after a name (also on the scoreboard) means they picked their team.
- **Size rule:** a side may be at most **2 players bigger** than the other (host setting *Max team size difference*, 0-4). Only people who picked a side count: bots and people on Auto fill the smaller side, so a group of friends can stack a little but not 6v2. A full side's button is greyed out with the reason. **Bots never block you:** join a side that has a bot and that bot moves to the other side.
- **Switching:** before the match (warmup, countdown, results) or while dead, at most once every **10 s**, never while carrying the flag. It's a respawn, not a suicide: no death, no score or kill streak change. While alive in a live match the buttons say to switch while dead.
- **Autobalance** only moves bots and people on **Auto** (people only while dead or between matches). Someone who picked a team is never moved; a new match keeps everyone's pick. People who join get the smaller side on Auto.
- **Host settings** (Host and Single player screens, shown for team modes; the host can also change them in Esc > TEAMS): **Team selection: Manual / Auto only** (default Manual; Auto only is the old automatic split) and **Max team size difference** (default 2). Saved in `settings.txt` as `team_select=manual|auto` and `team_max_diff=2`; older files get the defaults. Online, a client asks the server, which checks the rules and answers (protocol 20).

Maps can place team spawns, hills and flags by hand; otherwise the game works them out from the map (see Maps/README.md).

### Attachments (gunsmith)

Every weapon has up to six attachment slots: **Optic, Muzzle, Barrel, Underbarrel, Magazine, Stock**. Open a gun's attachments from the loadout screen: the **Edit attachments** button on the Primary or Secondary card, or **Attachments** (top right) on the weapon select. The attachments page shows the gun large, with how many attachments are unlocked and what's equipped; click a slot card to pick from it. Hover an attachment to see its pros, cons and the resulting stats.

- **Optics:** Pinhole Reflex (1.4x), Red Dot (1.5x), Holographic (1.6x), Brick 2x Prism, 2.5x ACOG, Tandem 3x Hybrid, 4x Scope, Variant 6x LPVO, 8x Sniper Scope and Longshot 12x (snipers only). Each has its own model, reticle and zoom, and the bigger the optic, the slower you aim.
- **Seeing through optics:** every optic is a physical sight on top of the gun. When you hold right click the gun comes up and the sight moves to your eye, housing and all, and you look through its lens: inside it you see only the world and a crisp reticle (no rail, receiver or tube insides), with a faint glass tint. The higher the magnification, the more of the screen the lens fills: reflex sights (Pinhole, Red Dot, Holographic) stay a small window on the gun, the 2x-3x prisms fill a good part of the screen, the 4x and 6x most of it, and the 8x and 12x all of it but a thin eyepiece rim in the corners. It's one continuous motion from the hip to full aim, with no switch to a flat overlay. Guns with a built-in scope work the same way. Each optic's look (reticle, how much of the screen its lens fills, rim, tint) is in its file's `sight` block ([Attachments/README.md](Attachments/README.md#sight-block)). Fitting an optic removes iron sights and LMG carry handles that would be in its way.
- **Muzzle:** Suppressor (much quieter shots, faint tracers, -15% range), Compensator (cuts the climb), Muzzle Brake (cuts sideways recoil and shake), Choke (shotguns).
- **Barrel:** Long, Short, Heavy. **Underbarrel:** Vertical Grip (cuts the climb), Angled Grip (faster aim, softer first shot), Laser.
- **Magazine:** Extended, Fast, Drum. **Stock:** Light, Heavy (less recoil, faster recovery), No Stock.
- The gunsmith's stat bars show each change in green or red with the percentage. However much you stack, a gun keeps at least 40% of its recoil and half its hip spread.
- Which attachments fit depends on the weapon class, and every class has some of its own: pistols get the Slide Dot, slide comp, extended and snub barrels and a brace or holster stock; shotguns chokes, a slug barrel, a drum and a shell holder; snipers the 12x, a match barrel, a straight-pull action and stripper clips; LMGs a quick-change barrel and a long belt box; and the specials (nailgun, arc carbine, musket, toy blaster) their own sights, muzzles, barrels, gyro and braces, down to a blunderbuss bell and a **bayonet** for the musket (the bayonet makes a melee stab reach 3.2 m instead of 2.4 m). Every attachment is a trade (each makes at least one stat worse), and the full class-by-attachment table is in [Attachments/README.md](Attachments/README.md#which-classes-take-what). Ammunition (AP, incendiary) only changes stats.
- **Unlocks are per weapon** with weapon XP (see Progression): 3-7 matches with a gun to unlock everything on it. Weapon XP is saved in `progress.txt`, and Reset level clears it.
- The server applies the same attachments to the same stats, so they're fully networked. Other players hear your suppressor and see your optic and grip.

### Loadouts

Open **LOADOUTS** from the main menu, or press **B** in a match. The screen lists your loadout slots on the left. Pick one to make it the loadout you spawn with; it's marked with an orange star. The selected loadout is shown on the right as cards: **Primary Weapon**, **Secondary Weapon**, **Perks**, **Lethal**, **Tactical** and **Kill Streaks**. Click a card to change that part, and **Rename** to name the loadout. Each loadout keeps its own attachments, and a gun you move into another loadout starts with the build you last used on it. Everything saves to `loadout.txt`. An older single-loadout file becomes slot 1.

- **More slots unlock as you level up.** Level 1 gets one slot, and the tenth arrives at max level, evenly spaced: levels 1, 6, 11, 17, 22, 27, 32, 38, 43 and 48. Locked slots show the level they unlock at.
- **Back goes back one page.** The screen is a stack of pages: the footer's **BACK** button (and **Esc**) returns from the attachments to the weapon select (or to the overview, if you came from a card's Edit attachments), from the weapon select, lethal, tactical, perks and kill streak pages to the overview, and from the progression page to whatever was open before. On the overview, BACK returns to the main menu (in a match it's **CLOSE** and closes the menu). A line at the top shows where you are, e.g. *Custom Loadout 1 / Primary weapon / Kestrel PDW attachments*.
- **Your progression is the bar at the bottom right:** LEVEL, XP into the level and the next unlock. Click it (*ALL UNLOCKS*) for the progression page: level, XP, stats and every unlock by level.
- **Edit attachments:** the Primary and Secondary cards have an **Edit attachments** button beside the gun's name, which opens that gun's attachments directly. Clicking anywhere else on the card opens the weapon select.
- **Picking a gun:** clicking the Primary or Secondary card opens the weapon select. Class tabs run across the top (each shows how many guns it has and how many are still locked). Below, a column of tiles shows each gun with this loadout's attachments on it, its level (attachments unlocked) and weapon XP bar, and an EQUIPPED, SELECTED or LOCKED tag (with the level it unlocks at). **Click a tile to select it**: the gun is shown big on the right (hovering a tile previews it too), with its stats compared to the gun you have equipped (green = better, red = worse), its magazine, reload time and shots to kill. The selected tile has a blue outline; hover is orange, and the equipped gun has an orange bar and the EQUIPPED tag. **EQUIP** (bottom right) puts the shown gun in the loadout; it reads EQUIPPED for your current gun and is greyed out for a locked one. **Double-clicking a tile does exactly the same as EQUIP** (the tile flashes, then shows EQUIPPED); on a locked gun it just shakes.
- **Attachments on the weapon select** (top right) opens the attachments of the gun shown. For a gun you haven't equipped yet, the page opens as a preview, and picking your first attachment equips the gun into this loadout with that attachment. It's greyed out for locked guns.
- **Kill streaks:** the Kill Streaks card opens the streak page. Your three slots are along the top, and every streak is below with its kill cost, what it does, and the level it unlocks at if it's still locked. Click a streak to carry it; when all three slots are full, it replaces the highlighted slot (click a slot to choose which). Click a streak you carry to drop it. See [Kill streaks](#kill-streaks).
- **Double-click always means "pick it"** across the loadout screen (lethal and tactical lists, perks, kill streaks, attachment lists), and the second click of a double-click never also clicks whatever the page change put under the mouse. Clicking something locked gives a dry click.

### Perks

Each loadout carries one perk from each of three categories. Every perk only works in one situation, but it's strong when that situation comes up. Perks light up on the HUD (under your health) while they're active, and a banner announces the big moments.

| Category | Perk | When | What it does |
| --- | --- | --- | --- |
| Movement | **Low Profile** (start) | Crouched or prone | Move 80% faster; Recon Pulses can't reveal you while you're low |
| Movement | **Head Start** (level 3) | Just spawned | 35% faster for the first 4 s of every life |
| Movement | **Parkour** (19) | Walls, windows, roofs | Reach ledges 0.6 m higher, climb twice as fast, jump about 55% higher |
| Movement | **Stalker** (11) | Aiming down sights | Keep 95% of your speed while aiming instead of 70% |
| Movement | **Dead Silence** (23) | Sneaking up | Others hear your steps, jumps and landings 75% quieter and from half as far; bots notice you behind them within 3 m instead of 7 m |
| Movement | **Lightweight** (33) | Carrying a heavy gun | Your gun never slows you below full speed (LMGs, snipers); no effect with light guns |
| Movement | **Afterburner** (36) | Just got a kill | 35% faster for 4 s after a kill; no refresh during the boost, 6 s cooldown after it |
| Movement | **Ghost** (45) | On the move | Recon Pulses and Recon Sweeps can't reveal you while you're moving (faster than 1 m/s) |
| Survival | **Flak Jacket** (start) | Grenades | 75% less explosive damage, including your own |
| Survival | **Quick Fix** (7) | Between fights | Health starts coming back 2.5 s after the last damage instead of 5 s |
| Survival | **Bloodthirst** (9) | Just got a kill | Each kill heals you to full |
| Survival | **Scavenger** (21) | Just got a kill | Each kill gives back one grenade and one equipment charge, up to what you spawn with |
| Survival | **Entrenched** (25) | Crouched/prone and still | 40% less bullet damage |
| Survival | **Hardline** (29) | Earning kill streaks | Every streak costs one kill less (never below 2) |
| Survival | **Last Stand** (41) | Once per life | The first lethal bullet or blast leaves you on 1 HP, and a shield blocks your next 3 bullet hits (up to 8 s); never saves you from melee |
| Survival | **Cold Blooded** (43) | Turrets and drones | Enemy Sentry Turrets and Attack Drones can't lock onto you (stray rounds still hurt) |
| Damage | **Point Blank** (start) | Within 8 m | Bullets and melee hit 40% harder |
| Damage | **Sleight of Hand** (5) | Reloading | Reload 35% faster (stacks with Overdrive) |
| Damage | **Opener** (14) | Enemy on full health | Bullets and melee hit 50% harder |
| Damage | **Steady Aim** (16) | Hip fire | 35% tighter hip-fire spread; aimed fire unchanged |
| Damage | **Demolitions** (27) | Grenades | Your Frag and Impact blasts reach 25% farther (Frag 9 m -> 11 m), same centre damage |
| Damage | **Deadeye** (31) | Beyond 30 m | Bullets hit 40% harder |
| Damage | **Tight Grip** (38) | Aiming down sights | 30% less recoil while aimed; hip fire unchanged |
| Damage | **Flanker** (47) | Hitting their back | Bullets and melee hit 60% harder |

Movement perks run inside the shared movement code (client prediction and the server agree). The server applies survival and damage perks, and bots carry perks that suit their personality. Handling perks (Sleight of Hand, Steady Aim, Tight Grip, Stalker) work on your own client like Overdrive does, and bots get Sleight of Hand, Steady Aim, Stalker and Lightweight too. Saved loadouts store perks by name, so older `loadout.txt` files load unchanged; the original perks keep their unlock levels.

**Last Stand in detail.** Once per life, the first bullet or blast that would kill you leaves you on 1 HP and raises a shield that blocks the **next 3 bullet hits** completely. One shot counts as one hit, so a shotgun blast uses a single charge however many pellets land, and every turret or drone bullet is a hit. **Melee and explosions go straight through the shield** and don't use charges: a knife or a grenade finishes off someone on Last Stand. Last Stand never saves you from a melee hit at all: melee kills in one hit. The shield lasts until its 3 charges are used or **8 seconds** pass, whichever comes first (so it can't carry over once health regeneration has refilled you). Three pips next to your health bar show the charges left. Everyone else sees a faint blue shell around a shielded player that flares each time it blocks a hit, with its own zap sound. Bots with Last Stand get exactly the same thing.

### Health regeneration

Health comes back on its own once you're out of the fight: **5 seconds** after you last took or dealt damage, you regenerate **8 HP a second** up to full (about 12 s from 1 HP to 100). Taking a hit, or landing one (a hit a Last Stand shield blocks counts too), restarts the 5 seconds; firing and missing doesn't. The health bar pulses and glows green while you regenerate. The server runs it, so it's the same for people and bots, in every game mode (including Gun Game and solo test matches), and dead players don't regenerate.

### Kill streaks

Each loadout carries **three kill streaks**. Kills in one life count up; reaching a streak's cost earns it, and an earned streak is **kept (even through death) until you use it**. Dying resets the count. Kills made *by* streaks never count toward the next one, and neither do kills after you've died (a grenade going off later, say). A streak kill gives normal kill XP.

Use an earned streak with its key: **4**, **5** and **6** by default, cheapest streak first (rebind them as *Kill streak 1-3*). Instant streaks go at once. The **Mortar Barrage** and **Strafe Run** start targeting: a marker shows where they'll land, **fire** calls them in, and aim or the streak key again cancels.

| Streak | Kills | Unlock | What it does |
| --- | --- | --- | --- |
| **Recon Sweep** | 3 | start | Every enemy on the map shows through walls for 10 s, to you and (in team modes) your whole team. The see-through silhouette is drawn in each revealed player's team colour (orange in free-for-all modes) and sits just inside their body, so you still see the model. Teammates are never revealed. Low Profile still hides people who are crouched or prone. |
| **Counter Sweep** | 4 | level 5 | Jams your enemies for 15 s: their Recon Pulses and Recon Sweeps show nothing, and their HUD fills with static. |
| **Overdrive** | 4 | start | 12 s of moving 35% faster and reloading twice as fast. Ends if you die. |
| **Mortar Barrage** | 5 | start | Look at a spot and fire. After 2.4 s, 7 shells land within 5 m of it, each whistling on the way in. Up to 160 damage each, falling off over 6.5 m; walls and roofs shield you, going prone helps, Flak Jacket works. |
| **Sentry Turret** | 6 | level 19 | Drops an auto-turret just in front of you, facing where you look. It covers a 150-degree arc out to 40 m, warms up for half a second on a new target, and fires 12-damage rounds. 45 s, 520 HP, armoured: bullets do 60% damage (a Vanguard AR needs 38 hits, more than a full magazine), explosives 150%. |
| **Attack Drone** | 8 | level 32 | A drone circles the map above the rooftops for 40 s, firing 3-round bursts (26 damage each) at any enemy it can see. 200 HP. |
| **Strafe Run** | 11 | level 46 | Look at a spot and fire: 2.6 s later a jet strafes a 50 m line through it, along the way you're facing (12 hits, up to 110 damage each). Roofs shield you. |

- **Everyone sees it coming.** A banner names whoever called in what ("Brick called in a Mortar Barrage", red when it's an enemy's), strikes put a red flare and danger marks where they'll land, and the turret and drone carry a light: green for yours, red for an enemy's.
- **Turrets and drones can be destroyed** with bullets, grenades and enemy strikes (no headshot bonus on machines). Your own bullets pass through yours. The kill feed shows who destroyed what. A health bar floats over yours and your team's at all times (over an enemy's once it's damaged), and while yours is up your HUD shows its health and seconds left above the streak icons.
- **The HUD** shows your three streaks at the bottom centre: key, kill cost, a pip per kill and a glow when it's ready, plus your current streak count. A banner announces each one you earn.
- **Bots** carry three random streaks and use them: mortars and strafe runs on the last enemy they saw or the biggest group, turrets facing where the enemies are.
- Streaks unlock with your level like perks: three to start, the others spread evenly. They're saved in `loadout.txt` (`loadout1.streaks=Recon Sweep, Mortar Barrage, Sentry Turret`); older files get the three starters, and a locked streak is swapped for a starter.
- Everything is decided on the server (counts, activation, damage). Strikes are planned from one random seed, so clients draw the shells and impacts without extra messages; turrets and drones send ~22 bytes each with every snapshot.

### Melee

Press **V** for a quick rifle-butt. It reaches about 2.4 m and **kills in one hit** (150 damage against 100 health), from the front or behind. Entrenched (bullets only) and Flak Jacket (explosions only) don't reduce it, and Last Stand doesn't save you from it. It cancels a reload, has a 0.8 s cooldown, and you can't fire for a moment after swinging. It's resolved on the server with the same lag compensation as shots. Zombies and breakable props still take 55 damage from it (a built Zombies melee weapon has its own stats). Bots use it when you stay right up close to them: they need a moment in reach first (0.6 s on Easy, 0.35 s on Normal, 0.2 s on Hard), so running past one isn't instant death.

### Bots

Bots play through exactly the same input commands as people (movement, mantling, stances, shooting, grenades, melee), so they follow the same rules. Difficulty (**Easy, Normal, Hard**) sets reaction time, aim error and turn speed. On top of that every bot has a **personality**, shown after its name, e.g. `[BOT] Brick (Rusher)`. A lobby gets a mix:

| Personality | Plays like |
|---|---|
| Rusher | Hunts the nearest enemy and fights up close with SMGs and shotguns; fast, twitchy strafing; barely retreats. |
| Marksman | Picks an open, preferably high perch, crouches or goes prone and holds long sightlines with DMRs and snipers; moves on after a kill. |
| Flanker | Takes side routes to get behind people (the path search avoids the direct line); commits to one strafe direction. |
| Anchor | Holds an open spot (or the objective) with rifles and LMGs, crouch-peeks, uses cover a lot, and only chases nearby. |
| Tactician | More grenades (including at where you ducked out of sight), pre-aims corners, retreats to cover to reload. |
| Chaotic | Any gun, any habit. |

Personalities also change engagement range, aggression, retreat health, strafing rhythm, cover use and perks (in Gun Game everyone gets the ladder's gun and the shared perks, so only the habits differ). In King of the Hill and Capture the Flag, each life a bot rolls whether it plays the objective or hunts, by personality: Anchors almost always go for it, Tacticians often, Rushers rarely. Bots navigate with a **waypoint graph** built for each map the first time it's played (a slice per tick, usually within a second or two; until then they steer straight): nodes on every surface you can stand on, linked where a player can really walk, step up, mantle (up to 1.7 m) or drop, checked with the game's own collision. They plan routes with A\*, cut corners when the way ahead is clear, climb ledges, step aside for each other in doorways, and re-plan when they get stuck. In a fight they keep to their weapon's range, strafe, duck behind cover to reload or when hurt, then peek back; out of a fight they chase where they last saw you, investigate gunfire, and glance around corners. They aim at the chest and drag their aim back down under recoil, so headshots are a minority of their kills.

### Respawning

- **Loadouts only change between lives.** During a life you can still edit loadouts, and the changes apply the next time you spawn. The exceptions are the warmup and the 5-second pre-match countdown, when changes apply immediately.
- **Death cam and kill card.** When you die, the camera rises over your body, then turns to face whoever killed you and zooms in a little, following them for about 3 seconds. A **kill card** shows the killer's name, their gun with its attachments (optic marked), headshot, distance, how much health they had left and their streak (melee, grenades, streak rewards and killing yourself get their own versions). Click, or press the loadout key, to skip straight to the loadout screen. On wide screens the card stays next to the loadout window.
- **Kill notices.** When you kill someone, **KILLED <name>** appears under your crosshair in their colour (team colour in team modes), with tags under it: HEADSHOT, MELEE, the grenade or streak that did it (FRAG GRENADE, SENTRY TURRET...), LONGSHOT and the distance for gun kills from 40 m, DOUBLE / TRIPLE / QUAD KILL for kills within 2.5 s of each other (their lines stack), REVENGE on whoever killed you last, and every 5 kills in a row. It fades about 2 s after your last kill, next to the +XP popups. Only the killer sees it (not in Zombies); everyone's kill feed (top right) names both players and fades out.
- **Nobody is force-respawned.** When you die, the loadout screen opens after the death cam. Pick a loadout, then click **RESPAWN**. The button counts down the 3-second respawn delay, and clicking early queues it. You also start every session on this screen.
- Close the screen to just watch. Press the loadout key to reopen it.

### Mantling

Hold **forward + jump** at a ledge up to 1.7 m high (crates, low walls, window sills) to climb onto it. Windows are 1.3 m tall, so you go through them in a crouch: you land on the sill and carry on through. Keep holding forward to drop out the other side. Mantling is part of the shared movement code, so it's predicted and smooth online too.

### Slopes

On terrain maps (the High Ground pack, the Dev pack's Terrain Test) the ground's slope is measured over your footprint (a 0.4 m radius), not the single bump under your feet, so small noise in a hillside doesn't stop you or start a slide.
- **Walkable up to 45 degrees.** Walking uphill is full speed up to 25 degrees, then gradually slower: about 92% at 30, 82% at 35, 72% at 40 and 60% straight up a 45 degree slope. Going across or down a slope is full speed.
- **Grip:** while you move, a narrow steep patch (walkable ground within 0.45 m before and after it) that rises no more than a step (0.55 m) against the slope around it is crossed, so brief steep bumps don't stop you or kick you into a slide, and small terrain lips and edges are stepped over like stairs. Taller banks (up to 1.7 m) are mantled with forward + jump, as before.
- **Stuck to the ground:** walking over a crest or down a slope (up to the 45 degree limit) keeps you on the ground; you don't bounce or go airborne.
- **Steeper than 45 degrees** slides you down. The slide is capped at 8 m/s and you can steer across and down it (not up), so you can always steer out. Cliffs are still walls.

Boxes, ramps made of meshes and the like keep the old 42 degree rule. Bots use the same rules: their nav graph only links what a player can walk, routes prefer gentler ways up, they cut straight across hillsides when the line is walkable, and a bot that gets no closer to its next waypoint (walking up and sliding back) re-plans another way.

### Lobby size

Up to **32 players** (people and bots), but every map has its own limit: **8-10** on small maps, **12-16** on medium ones and **24-32** on large ones, worked out from the map's area (a map file can set its own with `"maxPlayers"`, see [Maps/README.md](Maps/README.md#player-limits)). The lobby shows **x / max players** under the bot slider, and the slider stops at the map's limit. With **Random** from a pack (or **True Random**), it goes up to the biggest map in the pool and the game picks among the maps that take everyone. The map list shows each map's limit.

- **Joining a full game:** a person takes a bot's place. If the map is full of people, the join is refused with "The server is full: x/y players on map".
- **Map vote:** only maps big enough for the people playing are offered. When a smaller map wins, bots it has no room for leave (people never do), and they come back on a bigger map: the bot count you set is remembered.
- **Spawns:** every map needs a spawn per player. Maps with fewer get extra spawns when they load (same floor, in sight of a hand-placed spawn, clear of walls), so the old maps play as they always did with more people. Spawning never puts you on top of someone.
- **Team Deathmatch** plays to 5 kills per player in bigger lobbies (40 up to 8 players, 80 at 16, at most 100).
- **Scoreboard:** above 12 players it switches to two compact columns (Blue and Red in team modes, the ranking split in two otherwise). The kill feed keeps 6 lines, and a burst of joins shares one line ("X and 9 others joined").
- **Bandwidth:** each client receives about 9 KB/s at 8 players, 13 KB/s at 16 and 21 KB/s at 32 (measured by the self-test over loopback, game data without UDP headers). The host sends that to every client.

| Map | Limit | Map | Limit | Map | Limit |
| --- | --- | --- | --- | --- | --- |
| Proto Box | 8 | Harbor | 12 | Farmland | 32 |
| Scrapyard | 8 | Market Street | 12 | Downtown | 32 |
| Warehouse 9 | 8 | Trenches | 12 | Airfield | 32 |
| Courtyard | 10 | Train Yard | 12 | Village | 32 |
| Long Shot | 10 | Overpass | 12 | Industrial | 32 |
| Ziggurat | 10 | Floodgate | 12 | Highlands | 32 |
| Rooftops | 10 | Crossroads | 16 | Military Base | 32 |
| Blueprint | 10 | Quarry | 16 | Canyon | 24 |
| Galleria | 10 | Office Park | 16 | Citadel | 24 |
|  |  | Ruins | 16 | Confluence | 24 |
|  |  | Trefoil | 16 |  |  |

### Server browser and public games

**Multiplayer** has three tabs:

- **Server browser:** lists games to join with name, map (with an orange **True Random** tag when the host plays True Random), players / the map's limit (and bots) and ping. Click Join. Games on your network show up automatically: the browser broadcasts on the local network and every hosted game answers. Public internet games show up when a master server is set (see below). The list refreshes every few seconds.
- **Host a game:** the map (**Change map...** opens the [map select screen](#map-select): random from a pack by default, one map, or True Random), port, UPnP, bots, **server name**, and **Advertise server**, which lists the game publicly through the master server.
- **Join by address:** type an IP and port directly.

**About public listing:** a public list needs something always online that hosts can report to and browsers can ask. That's the **master server**, built into the game: run `CallOfSlop.exe --master` (optionally followed by a port; the default is UDP 27050) on any always-on PC with that UDP port reachable. It tries UPnP itself. Then everyone enters that PC's address under **Settings > Player > Master server**. The master only stores addresses: names, maps, players and pings come straight from each game. Without a master server, the browser still finds every game on your LAN.

Single player works the same way: **Change map...** shows the current choice and opens the map select screen. With random picked, the lobby background tours the pack's maps (True Random: every pack's).

### Map select

**Change map...** (Single player and Host a game) opens the map select screen:

- **Left: the map packs**, one tile each in their `pack.json` order (Dev last), with the pack's name, map count, sizes ("7 small, 9 medium, 8 large") and a small picture: a collage of up to four of its maps, seen from above. **True Random** sits at the bottom.
- **Right: the open pack.** Its description, then **Random** at the top, then its maps, smallest first, each with a picture, size, player limit and description. Opening a pack always pre-selects **Random**. A map too small for your bots is shown in orange (the bots are cut to fit).
- **Click** selects, **double-click** (or **Enter**, or the orange **CHOOSE** button) takes it and returns to the lobby; double-clicking a pack tile takes that pack's Random. **BACK** or **Esc** leaves without changing anything. Keyboard: **Up/Down** through the maps, **Left/Right** (or Page Up/Down) through the packs. The lobby background follows the selection, and CURRENT marks what's chosen now.
- **True Random** is a pack that isn't a folder: every match is on a random map from **all** packs except **Dev** (its test maps aren't balanced for play; you can still pick them from the Dev pack). Like random from a pack, it only picks maps that take you and your bots, and the map vote afterwards offers maps from every pack. A pack can leave itself out with `"trueRandom": false` in its pack.json.
- The pictures are rendered by the game itself (no image files): each map once, into a small offscreen texture at 384x216, one per frame so the screen never freezes, and cached until the map's file changes. If the graphics card can't do it, plain coloured tiles are shown instead.

The choice is saved in `settings.txt` as before (`map_pack=<pack name>`, `map=<map>`, `map_random=1|0`); True Random is `map_pack=*all`. Older settings files load unchanged.

### Map vote

At the end of every match the results screen shows **three maps to vote on**, taken from the pack of the map you just played (with **True Random**: from every pack but Dev; from every map if that pack is too small): one small, one medium and one large where possible, never the map you just played. Click one or press **1 / 2 / 3**. You can change your vote until the 12-second timer runs out, and the live tally shows which map is leading. Only maps big enough for the people playing are offered (see [Lobby size](#lobby-size)): a map's player limit in the current mode must be at least the number of **human** players here, counting the host and anyone still loading in; bots don't count (they leave a smaller map and come back on a bigger one). If fewer than three maps are big enough the ballot has fewer options; if none is, the map you just played is offered again when it is big enough, otherwise the largest maps (the server log says so). If someone joins during the vote and an option becomes too small, that option is swapped in its slot for another map that fits (votes for it are dropped, so those players vote again; other votes keep counting), or removed if no other map fits. The same rule applies if the winner can't be used (a map file removed meanwhile): a random map that fits is used instead. Only human players vote; bots never do and aren't counted. Once every human has voted, the timer drops to 3 seconds. The next match starts on the map with the most votes, with ties broken at random among the tied maps; if no human votes (say, a solo host who doesn't click), one of the three is picked at random. Players who join during the results screen see the ballot and can vote.

### Maps

All maps share the same primitive style: everything is a box, with the grey-and-orange dev grid. Buildings are walk-in, with doors, windows to shoot through, internal stairs between floors and, on many, a staircase onto the roof. Pick the map in the lobby (Single player or Host, **Change map...**); the background previews your choice.

**Map packs.** Each folder in `Maps` is a map pack, and on the [map select screen](#map-select) you first pick a pack (shown with its map count by size, for example "7 small, 9 medium, 8 large", and a picture), then play a random map from it or choose one, or pick **True Random** for any map of any pack. The 24 shipped maps are the **Classic** pack (`Maps/Classic`). Map files placed straight in `Maps` form a pack called **Other**. A folder can hold a `pack.json` with the pack's name, description, author and sort order; without one the folder name is used. To share a pack, zip its folder; to install one, unzip it into `Maps` and click **Refresh maps**. The Classic maps:

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

**High Ground pack** (`Maps/HighGround`). This pack has six maps on real terrain: hills, cliffs, valleys and gullies that shape the fights, plus foliage and a few lifts. Every map has hand-placed spawns for its player limit and objectives for every mode, and is dressed with props that fit its theme (pallets and drums in the quarry, hay and logs at the farms, sandbags and road barriers at the outpost...), placed mirrored so both teams get the same cover. [Maps/HighGround/DESIGN.md](Maps/HighGround/DESIGN.md) explains each layout (collision points, chokes, flanks, sightlines).

**Cluttered pack** (`Maps/Cluttered`). Maps with no square edges, packed with props and foliage to see how much life a map can hold. **Carousel** (small, 10 players) is an abandoned fairground in a round clearing: a carousel you can ride turning in the middle, game stalls, food trucks, two bandstands and a caravan, with about 2,100 props and 14,500 plants, built as one half turned round for the other team. **Sprawl** (large, 32 players) is an irregular, noisy blob with two bays and two peninsulas on real terrain: overgrown ruins, a pond park, a packed market in the waist, a forest and a junk field, with about 5,000 props and 70,000 plants. It isn't mirrored: the teams are balanced by walk distance to every objective and by the cover along their routes (checked by the self-test's **Cluttered pack** section, with a 32-player bot match). **Cul-de-sac** (small, 10 players) is a suburban turning circle on trash day inside a keyhole-shaped hedge: walk-in houses with kitchens, living rooms and a garage, lawns, drives, parked cars, bins at the kerb, backyards with a pool and a duck pond, mirrored across the street. **Dockside** (medium, 12 players) is a harbour front curving round a bay: quays, piers and a lighthouse mole over the water, container yards under cranes, warehouses and a fish market you can fight through, with about 2,700 props and 16,000 plants, mirrored for the two teams. **Old Town** (large, 16 players) is an overgrown small-town centre at dusk: Main Street's diner, barber, arcade, laundromat and corner store (all walk-in and furnished), the square with its fountain and clock, a town hall with a bell and a church, a park with a duck pond and playground, a gas station, a car park and a rail crossing, every prop placed where it belongs (bins by the back doors, lamps and parked cars along the kerbs, AC units on the roofs). **Supermarket** (medium, 12 players) is a run-down SLOP MART inside a noisy polygon: grocery aisles, produce, bakery, dairy, a deli under a staff mezzanine, a frozen section, checkouts, the stock room and loading dock, and a weedy car park out front; it isn't mirrored (Blue comes in from the car park, Red from the dock) and is balanced by measured walks, cover and sight lines instead. **Powder Keg** (large, 16 players) is a warehouse full of explosives: pallet racking, catwalks and offices in the hall, trucks at the dock doors, a chemical store and a propane tank farm, with nearly 300 fused barrels, propane tanks and gas cylinders that set each other off in chain reactions; it isn't mirrored (Blue attacks from the loading yard, Red holds the offices and catwalks) and is balanced by measured walk times. **Truck Stop** (medium, 12 players) is a desert highway truck stop at dusk, not mirrored: Blue holds a two-storey motel with balconies round a drained pool, Red a lot full of semis and trailers to weave between and climb, with a diner, a gas station and store, a tire shop with a reachable roof and a jack-knifed semi on the highway in between, balanced by walk distance, cover and sight lines. **Timberline** (large, 16 players) is a pine forest with little log cabins all over, a creek, a lake with a dock, canoes and ducks, and two bunkers hidden underground (down through a hatch in a cabin floor, or along a trench to a hillside door or a culvert in the creek bank); Blue's ranger station by the lake faces Red's logging camp up the hill, balanced by measured walks and cover instead of mirroring. **Deepcut Mine** (large, 16 players) is a mine site on a dusty afternoon, not mirrored: the workers' camp and the processing plant down low for one team, a terraced massif with the headframe (its sheave wheels turning, a cage running up the shaft) high up for the other, an open pit and spoil dumps between them, and timber-braced tunnels that dive into the hills and come out on other levels; the teams are balanced by measured walks to every objective instead of mirroring. **Beachhead** (large, 16 players) is a beach that leads up into a military base and isn't mirrored: Blue lands among wrecked landing craft and tank traps, Red holds the base 12 m up on the bluff (barracks, HQ, motor pool, fuel depot full of explosive barrels, radar, helipad, watchtowers, pillboxes dug into the cliff top), with five ways up (the switchback road, a ravine, a bluff path, the dune slope and a drainage tunnel under the bluff) and the objectives where both teams walk the same distance. [Maps/Cluttered/DESIGN.md](Maps/Cluttered/DESIGN.md) explains the layouts.

| Map | Size | What it is |
| --- | --- | --- |
| Gravel Pit | 72 x 64 m, small | A terraced quarry pit with a crusher plant you walk under, haul roads, goods lifts and safety berms. |
| Hillfort | 68 x 68 m, small | A ruined keep on a round summit, with a ditch and rampart round it, two gates and breaches. |
| Riverbend | 120 x 100 m, medium | An S-shaped river with a humped stone bridge, two fords, cliff banks and watermills. |
| Adobe Mesa | 120 x 96 m, medium | A pueblo on a cliff-sided mesa in a red canyon, with ramps, hoists, buttes and an arroyo. |
| Ridgeline | 176 x 160 m, large | A summit outpost on a long ridge, with trench lines, a rock band, and a checkpoint on each pass. |
| Alpine Pass | 160 x 184 m, large | A mountain hut on a snowy col between granite massifs, with rock bands, a ravine and a cliff ledge. |

The lobby groups maps by size. Medium and large maps add distance haze so the far side fades into the sky. Bots find their way around every map with a waypoint graph and A\* (see [Bots](#bots)).

**Maps are data files.** Each map is one `.json` file in a pack folder under `Maps`: its name, size, sky colour and a list of building ops (`Building`, `WallX`/`WallZ` with door and window holes, `Slab` with stairwells, `StairsX`/`StairsZ`, `Container`, `Car`, `Hill`, `Tree`, `Watchtower`, `Hangar`, `Bridge`, `TrainCar`...). To add a map, drop a file into a pack folder (or a whole pack folder into `Maps`) and click **Refresh maps** in the lobby; no restart or recompiling. In single player, **Reload map files** in the Esc menu rebuilds the map you're standing in from its file, for quick edit-and-look map making. Maps can also have **terrain**: an optional `"terrain"` object shapes the ground with hills, ridges, valleys, plateaus, cliffs, ramps and noise (a heightfield you walk on, shoot over and throw grenades across; slopes up to 45° can be walked, a bit slower uphill, steeper ground slides you down and cliffs are real walls; see [Slopes](#slopes)), and the ops are placed on the ground. The **Dev** pack's *Terrain Test* map shows it off. Terrain can also have **holes and caves** (`hole` and `cave` features): pits, sinkholes and skylights, and tunnels and chambers under the ground with rock walls and ceilings that get darker inside, which players, bots, bullets and grenades all use; the Dev pack's *Cave Test* map has a tunnel between two entrances through a chamber, a sinkhole into a side passage and a few pits. Anyone can make one: **[Maps/README.md](Maps/README.md)** is the map makers' guide, with a full example and every op. A file with a mistake is skipped with a message that names the file and the problem (also shown in the lobby), and `CallOfSlop.exe --check-map FILE` checks one without starting the game. Online, servers and clients identify maps by name plus a geometry fingerprint, so a player missing a host's custom map is told which file to get, and the server browser shows maps you don't have in orange.

**Foliage.** A map file can also have a `"foliage"` section: grass, bushes, shrubs and trees scattered over areas, or placed one by one (see [Maps/README.md](Maps/README.md#foliage)). Only tree trunks are solid. The rest is drawn with instancing, culled in 16 m chunks, thinned out with distance through a dither (no popping) and sways in the wind. The **Dev** pack's **Foliage Test** map is a flat 100 m meadow for trying it out. **No-go zones:** nothing grows indoors: plants under a roof, a floor above, a bridge deck or an awning, or inside a box or a cosmetic mesh, are left out (trees don't count as roofs), and paved surfaces thin them out by material (intact concrete, tiles and floors grow nothing; a map can mark cracked paving or busted paths with a small fraction, so an odd tuft or shrub comes through). Maps can add explicit `exclude` and `include` zones and turn the automatic rules off (see [Maps/README.md](Maps/README.md#no-go-zones)). Maps without foliage are unchanged. Lighting (sun, sky and ground ambient, fog) is shared by boxes and plants (`Source/Render/Lighting.cs`).

**Skies.** A map file can have a `"skybox"` section: a drawn sky with a zenith/horizon gradient, a sun disc and glow placed by a direction or by time of day and latitude, a moon with phases, stars and drifting clouds, or an image sky (a cubemap of 6 PNGs, or a PNG/HDR panorama). Presets (`clear`, `overcast`, `dusk`, `night`, `desert`, `alpine`) cover the common cases: `"skybox": "dusk"`. The map's lighting follows its sky (sun direction and colour, ambient), and fog fades distant things into the sky colour behind them. Maps without a skybox keep their flat sky colour and look exactly as before. The **High Ground** maps each have one, and the **Dev** pack's **Sky Test** map is made for trying them out (see [Maps/README.md](Maps/README.md#sky)). **Settings > Video > Sky quality**: Low draws the gradient, sun and moon; High (default) adds clouds and stars. The sky is drawn last in the opaque pass, only where nothing else covers the screen, and costs well under 0.2 ms of GPU time (`[sky]` lines in the log; `Source/Render/Sky`).

**Moving platforms.** A map file can have a `"movers"` section: rotating platforms, sliding platforms (back and forth, or round a looping path) and elevators, each built from a few boxes and running on a constant loop (see [Maps/README.md](Maps/README.md#movers)). Stand on one and it carries you (a turning platform turns you and your view with it), walk around on it as on solid ground, and jump off with its momentum. They push you out of their way rather than passing through you, and never crush you into a wall. Bullets, grenades and sight lines hit them where they are. Their motion is worked out from the server's clock on every PC, so they cost no network traffic and your own movement on them is predicted without jitter. Bots keep out of their paths. The **Dev** pack's **Physics Test** map has slow and fast spinning discs, a spinning bar to jump over, sliders, a ferry across a gap and a looping elevator up to a deck.

**Props.** Trash cans, dumpsters, wheelie bins, crates, pallets, oil drums, cones, barriers, benches, lamps, hydrants, bricks, rubble, newspapers, bottles, cardboard boxes, chairs, tables and more: 298 props ship in the `Props` folder, one JSON file each in category folders: the 61 starter props (`Street`, `Industrial`, `Debris`, `Furniture`, `Nature`), the indoor and everyday set in `Household`, `Office`, `Kitchen`, `Garden` and `Misc`: furniture, appliances, office kit, kitchen things, garden things, ducks and pets (see [Props/README.md](Props/README.md)), plus 114 outdoor props in `Vehicles` (cars of every body type, a bus, trucks, a forklift, bikes), `Rooftop` (air conditioners, vents, air ducts, chimneys, a water tower, satellite dishes, solar panels), `Utility` (power poles, transformers, manholes, fire escapes, scaffolding, ladders), `Signage` (traffic lights, road signs, a billboard, shop signs) and `Urban` (a bus shelter, a phone booth, a newsstand, an ATM, fences, barriers). Maps place them one by one or scatter them with a `Clutter` op that keeps them clear of spawns, routes and doorways (see [Maps/README.md](Maps/README.md#props)). Solid props block players, bullets and bots like any box; small litter is cosmetic and fades out with distance. All of it is drawn instanced and culled in 24 m chunks, a few draw calls for hundreds of props; the game logs a `[props]` line every 5 seconds on maps with props. **Breakable props** (crates, boxes, bins, bottles, chairs, pallets, cones) break when shot, hit or blown up and shatter into pieces that bounce, settle and fade out; the explosive barrel goes off. The host decides what breaks and tells everyone, and every match starts with everything whole. Health follows the material: glass and paper break at a touch, plastic and wood take a few shots, metal more, and vehicles, concrete and big structures are unbreakable cover (the material health guide is in [Props/README.md](Props/README.md#material-health-guide)). The **Dev** pack's **Prop Gallery** shows the starter props, **Prop Gallery 2** the outdoor set, **Prop Gallery 3** the indoor set, **Prop Yard** is a small dressed street. The Classic and Collision Point maps don't use props. **Animated props** (`Props/Animated`, 65 of them: fans, roof turbines, a windmill and a wind turbine, flags, banners, swinging signs and lanterns, flickering lamps, neon signs, beacons, traffic and crossing lights, radar and satellite dishes, clocks, a ferris wheel and a carousel, a pumpjack, conveyor rollers, a shaking generator, a cement mixer, swings, wind chimes, a pendulum clock, a Newton's cradle, bobbing buoys and ducks, screens and arcade machines...) have parts that spin, swing, bob, shake, blink, pulse, flicker or cycle, set by an optional `"anim"` block on a part (see [Props/README.md](Props/README.md#animation)). It's cosmetic: colliders never move. The motion is worked out on the graphics card from the time and each prop's position (so a row of fans doesn't turn in lockstep, and flags and turbines follow the same gusts as the grass), costs nothing on the CPU, and props farther than 80 m (per prop) hold still and are drawn by the plain prop shader. The `[props]` line adds the animated props drawn moving and still and the animated pass's GPU time. The Dev pack's **Animated Props** map shows them all; existing maps don't use them. **Flags** fly national flags from a flag pool (`Props/Flags/flags.json`: the US, UK, French and Finnish flags): a Flag on a Pole picks one from its position (the same on every PC and every match, and mirrored flags match), and a map can name the flag per placement or limit its pool (see [Props/README.md](Props/README.md#flags)). The Cluttered and Dev maps use it; the Dev pack's **Flag Lineup** shows all four.

**Map shapes.** A map doesn't have to be a rectangle: an optional `"boundary"` section makes the playable area a circle, an oval, a polygon (concave is fine) or a smooth blob such as a bean, optionally with a seeded wobble so the edge is irregular, shown as a wall, a terrain cliff, a fence, a hedge, a line of boulders or props, or nothing (see [Maps/README.md](Maps/README.md#boundaries)). An invisible barrier runs exactly along the outline, so players, bots, grenades and debris never leave, and spawns, waypoints, foliage, clutter and the bots' routes all stay inside. The floor is drawn following the outline with a darker ground beyond it fading into the fog. Maps without a boundary are unchanged. The **Dev** pack's **Shape Test** map is a bean with a curved wall.

**Meshes and models.** Maps can also place round and sloped shapes and real 3D models, not only boxes: the `Mesh` op makes cylinders, cones, spheres (UV and geodesic), capsules, rings, ramps, pipes and rounded boxes from a few numbers, and the `Model` op places glTF 2.0 models (`.glb` / `.gltf`) from the `Models` folder, with their node hierarchy, materials and base colour textures (see [Maps/README.md](Maps/README.md#meshes-and-models) and [Models/README.md](Models/README.md)). Three small test models ship (a barrel, a traffic cone, a lamp post). All meshes share one GPU vertex and index buffer and are drawn instanced, one draw call per mesh and LOD, culled outside the view and switched to simpler LODs with distance, lit and fogged like the boxes; the game logs a `[mesh]` line every 5 seconds on maps with meshes. They are cosmetic for now (no collision), so map makers put a `Block` inside where players need cover. The **Dev** pack's **Mesh Test** map shows every shape plain and textured, a LOD line, the models and a field of 128 instanced shapes.

**Buildings.** Maps can place whole furnished buildings from the `Buildings` folder with one op (`{ "op": "Building", "name": "Small House", "at": [x, z], "yaw": 180 }`): walls with real door and window openings, floors, ceilings, stairs, roofs (flat, gable, hip, shed or none), porches, awnings, balconies and mezzanines, and furniture placed room by room by type (a bedroom gets a bed against the far wall with bedside tables and lamps, a kitchen a run of counters with a kettle on them...), never in a doorway or in front of a window. Each building is one folder (`building.json`, plus its own props and furnishing rules if it needs any; see [Buildings/README.md](Buildings/README.md)), with palettes, seeds, mirroring, turning and floor counts for variants. **Doors** open and shut with the interact key (E) and swing on their hinges; shut, they stop players and bullets; bots open them as they go; bullets, melee and grenades break them (wood takes about 5 rifle hits, metal 14), and a broken doorway stays open. **Window glass** stops you but not bullets: shoot it out (it shatters) and climb through the empty frame. The host decides and tells everyone, players joining mid-match get the doors and windows as they are, and every match starts with everything whole. The bots' routes run through doorways and up every staircase, and no foliage grows indoors. Building walls are drawn instanced (a whole street of buildings is a handful of draw calls; the game logs a `[buildings]` line). Ten starter buildings ship: a small house, a two-storey house, an apartment block, a corner shop, a diner, an office building, a warehouse with a mezzanine, a gas station, a garage and a ruined house; the **Dev** pack's **Building Yard** shows them all with variants. `tools/buildings` previews and checks building files without the game.

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
| E | Interact: open or shut the building door you're looking at (Zombies: push cart, slot station, buying) |
| F3 | Ready up (warmup) |
| 4 / 5 / 6 | Use a kill streak (cheapest first); for aimed ones, then fire to call it in |
| B | Loadout menu |
| Tab (hold) | Scoreboard |
| Esc | Close the menu / release the mouse (click to recapture) |

### Display

- **Windowed:** pick a size from your monitor's resolutions, or drag the window edges. The game remembers the size and position.
- **Borderless fullscreen:** a frameless window covering the monitor at its native resolution. Alt-tab is instant.
- **Fullscreen:** exclusive fullscreen at the resolution you choose.
- **UI scale:** menus and HUD scale with the window height automatically (about 1.2x at 1080p, 1.6x at 1440p). The slider adjusts on top of that, from 0.6x to 2x, and applies when you let go.
- **Foliage:** **Off / Low / Medium** (default) **/ High**. How much grass, how many bushes and shrubs, and how far away they're drawn, on maps that have foliage (try the **Dev** pack's **Foliage Test** map). Off draws none of it and costs nothing. Tree trunks are always there, because they're solid: they stop players and bullets. Grass, bushes and tree tops never do. The game logs a `[foliage]` line every 5 seconds on foliage maps (plants drawn, draw calls, CPU and GPU time) in callofslop.log.
- **Head bob:** 0-100% (default 100%). How much the camera bobs as you move: two small dips per stride, timed to your legs' footfalls, and a slight side-to-side sway and roll once per stride. It grows with ground speed, from about 1 cm and 0.2 degrees at a walk to about 4.5 cm and 0.8 degrees at a full sprint. There's none in the air (jumping, falling, mantling) or while prone, half while crouch-walking, and it fades out as you aim down sights, so scopes stay steady. It eases back in after you land. Only the picture moves: your aim and your bullets always come from the steady eye, and nothing is sent over the network. 0% is the old, perfectly steady camera. Saved as `head_bob` in `settings.txt`; older settings files load with 100%.
- **HUD width:** on ultra-wide monitors the in-match HUD (session info, kill feed, health, ammo, match bar, banners, scoreboard) stays inside a centred area so it's easy to read. **16:9** (default) keeps it 2560 px wide on a 3440x1440 or 5120x1440 screen; **21:9** allows 3360 px; **Full width** uses the screen edges. On 16:9 or narrower screens all three are the same. The crosshair is always centred, and the damage effects always cover the whole screen (a scope's lens is sized from the screen height, so on an ultra-wide screen more of the eyepiece shows at the sides).

- **Name tags:** **All** (default) **/ Team only / Off**, plus **Teammate health bars** (off by default). Names float over players' heads, CoD style:
  - **Teammates** (everyone in Zombies) are tagged whenever they're on screen within 60 m, *through walls*, so you can find your friends. In their team colour (in Zombies everyone is on the one co-op side: the friendly blue, also in the HUD's points list and the scoreboard), fading from 30 m to 60 m.
  - **Enemies** (everyone in free-for-all and Gun Game) are tagged only while your crosshair is on them, they're within 40 m and you can actually see them: walls and smoke hide the tag, so it never gives anyone away. The tag lingers half a second (fading) after your crosshair leaves, and drops at once if they step behind cover. An enemy shown through walls by your Recon Pulse or Recon Sweep can be tagged by aiming at their outline. In the enemy team's colour (red without teams).
  - Bots show their names ("[BOT] ..."). Never your own tag, never on dead players. Through a magnified scope tags stay but are a quarter smaller. While you're dead, teammates stay tagged from the death camera and your killer is tagged.
  - Tags shrink with distance and scale with the window height like the HUD (so they're the same size on 1080p, 1440p, 21:9 and 32:9), stay inside the screen, and a tag that would cover a nearer one stacks above it or fades.
  - Cheap: one ray-box test per enemy and at most one line-of-sight ray every 0.1 s (for the enemy you're aiming at), no allocations. Saved as `name_tags` and `name_tag_health` in `settings.txt`; older files get All. The distances and sizes are data in `UI/NameTags.cs` (`NameTagRules`).

### Stances

| Stance | Height | Speed | Spread | Recoil | Notes |
| --- | --- | --- | --- | --- | --- |
| Standing | 1.8 m | 100% | 100% | 100% | Can jump and sprint |
| Crouched | 1.2 m | 50% | 75% | ~85% (per gun) | Fits behind low walls and crates. LMGs lose almost all sideways recoil |
| Prone | 0.6 m | 22% | 50% | ~65% (per gun) | Can't go prone in mid-air. Your hitbox is long and low, and your head is out front |

Standing speed is 6 m/s walking and **13.5 m/s sprinting** (before your weapon's mobility, aiming and perks); stance percentages are of the walking speed.

You can't stand up where there's no headroom (for example under a container roof); from prone you'll get up to a crouch if that's all that fits. Stance is part of the shared 60 Hz simulation, so prediction, the server and lag compensation (which rewinds stance as well as position) all agree. Bots crouch for long-range shots; Marksmen and Hard snipers go prone, and bots crouch-peek in close fights and duck behind low cover to reload.

### Progression

You earn XP in **single player and multiplayer** alike. There are two tracks, both filled by the same play:

- **Account XP (levels):** +100 per kill, +50 more for a headshot, +25 more for a grenade kill, **+150 for finishing a match** and **+250 for winning it**. **Each level unlocks a weapon, grenade or equipment**, 48 levels in all. Perks, kill streaks and loadout slots unlock on top of that, spread evenly over the same levels. A level costs ~2,400-3,800 XP.
- **Weapon XP (attachments):** each gun earns its own XP: +100 per kill with it, +50 for a headshot, plus +150 per match for the primary you carried (+75 for the secondary). Its attachments unlock along a curve, with the first few quickly and the last ones slower.

The pacing is a rough target rather than a hard rule. A typical match is around 7 kills, which is ~1,000 XP on each track. So:

- **A gun is fully kitted out in 3-7 matches.** Guns with few attachment options (pistols) are on the short end, and guns with lots (rifles, DMRs) on the long end.
- **A new level, and with it a new weapon, comes at about the point your current gun is ~60% kitted.** That works out to roughly every 2-3 matches, so you get time with each gun before the next one shows up.

The lobby shows your level and next unlock, and the level bar at the bottom of the loadout screen opens the progression page, which lists every level. In a match, XP pops up by the crosshair, and banners announce level-ups and new attachments. Progress is saved to `progress.txt`, with weapon XP stored by weapon name (`wx.<name>=...`). Older saves carry over: their numbered weapon XP is converted to names, and old per-weapon kill counts become weapon XP.

- **Level 1:** BR-1 Prototype, Vanguard AR, Hornet SMG, Breacher Pump, Marksman M14, P9 Sidearm, Mk.23 Tactical.
- **Levels 2-48:** the other 43 weapons plus the Frag Grenade (level 4), Recon Pulse (8), Impact Grenade (13) and Smoke Grenade (20), in a fixed order. The fun ones come last: Grinder Rotary, Railspike, Toy Blaster.
- Locked items appear as `[LOCKED] ... level N` (weapons) or `[LOCKED] ... N weapon XP` (attachments). Bots ignore progression.
- Weapons added as files unlock at the level their file asks for (`unlockLevel`), or at level 48.
- Unlocks are kept on each player's own PC.

### Prestige

At max level (48) the lobby's **PRESTIGE** button (under the level bar; greyed out before max level) resets your account for a hat. A confirmation dialog says what resets and which hat you get ("Prestige 1: unlocks the Beanie"). There are five prestiges, one hat each:

| Prestige | Hat |
| --- | --- |
| 1 | **Beanie**: a small red knit beanie with a folded cuff and a bobble |
| 2 | **Baseball Cap**: blue crown, brim forward |
| 3 | **Fedora**: pinched crown with a black band and a wide brim |
| 4 | **Top Hat**: tall black cylinder with a red band |
| 5 | **Ridiculous Top Hat**: about 3.5 times as tall as the top hat (1.4 m), sways as you move |

- **What resets (the CoD rule):** level and account XP go back to level 1 / 0 XP, and every gun's weapon XP is cleared, so weapons, grenades, equipment, perks, kill streaks, loadout slots and attachments all lock again and are earned again. Loadouts fall back to unlocked items on their own (as after Reset level).
- **What you keep:** your hats and your lifetime stats (kills, headshots, matches, wins). Settings > Reset level keeps your prestige too.
- **Wearing a hat:** pick it in the loadout's cosmetics slot (None or any unlocked hat; locked ones show "Prestige N"). Others see it on your head in third person, in the death cam and kill cam, and lying by your corpse; you never see it in first person.
- **Cosmetic only:** hats have no collision and don't change your hitbox, and prestige gives nothing else. Bots never prestige and never wear hats.
- Your prestige shows as **P2** before your level: in the lobby, on the HUD level line and on the scoreboard (`[P2 L13]` after a person's name; bots get no tag).
- Saved in `progress.txt` as `prestige=N`; older saves load as prestige 0. Hat models are data: `Cosmetics/hats.json` (format `call-of-slop-cosmetics/1`; boxes in head space, `"round"` parts made of a few turned boxes).
- **Network:** your client sends its prestige and level after its name in the Hello; the roster carries prestige, level and hat for every player (3 bytes each, nothing in the snapshots). The server only uses the reported prestige for what it shows and to check the hat you pick (`Cosmetics.IsUnlocked`); it never affects gameplay. Levels on the scoreboard are as of joining (the host's own is live).

### Grenades and equipment

The loadout has a **Grenade** slot (2 per life) and an **Equipment** slot (1 per life), both refilled when you respawn. Projectiles are simulated by the server: they bounce off walls and floors, and blasts are blocked by walls. Everything is thrown at 22.5 m/s (plus a little lift and half your own speed), which carries about **34 m** on flat ground at the best angle (about 40 degrees up); a frag thrown that far lands with a few tenths of a second to spare before it goes off.

| Item | Unlock | What it does |
| --- | --- | --- |
| Frag Grenade | Level 3 | 2.5 s fuse, bounces. Up to 200 damage, kills within about 4 m, hurts out to 9 m. Your own frag hurts you at half damage, and going prone cuts blast damage by 30%. |
| Impact Grenade | Level 8 | Explodes on first contact. Up to 110 damage within 4.5 m. |
| Smoke Grenade | Level 13 | Pops a 5 m cloud for 14 s where it lands (1.2 s after the throw, or on landing if it's still in the air then). Blocks vision, and bots can't see through it. |
| **Recon Pulse** (equipment) | Level 5 | Throw it anywhere; it sticks where it lands. For 12 s, every enemy within 18 m of it shows to you through walls as a silhouette in their team colour (orange in free-for-all modes); teammates are never revealed. It beeps once a second, so enemies can hear where it is. |

Bots on Normal and Hard throw frags (and sometimes impacts) at targets 6-15 m away, picking the angle that makes the grenade go off at the target (bounces and roll included); Tacticians throw more, also at where an enemy just ducked out of sight (up to 22 m away). Explosions shake your screen when they're close.

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

Every weapon has its own stats: damage and headshot multiplier, pellets, fire rate and mode (semi/burst/auto), magazine, reload, hip and aimed spread, recoil, damage falloff, movement speed, zoom and draw time.

**Recoil is per weapon.** Each gun has its own seeded spray pattern, so holding the trigger always traces the same learnable shape, plus jitter, a first-shot kick and stance and aim multipliers. Assault rifles climb steadily with a gentle drift. SMGs kick little per shot but jump around. LMGs climb hard, but crouched or prone they barely move sideways. DMRs snap once and settle fast, snipers and shotguns punch the camera, pistols flip, and the specials each have their own feel (the Arc Carbine snakes, the Grinder spins up). When you stop firing, the aim drifts most of the way back to where you started. The camera shake and punch are only visual and never move your aim. Each weapon file's `recoil` block holds the numbers (see [Weapons/README.md](Weapons/README.md)).

**Weapons and attachments are data files.** Each weapon is one `.json` file in the `Weapons` folder (one subfolder per class), and each attachment is one file in `Attachments`, holding its slot, which weapons it fits and the stat changes it makes. To add a weapon, drop a file in and restart. Its sounds are generated from its stats and its model from its `model` block (an archetype like `pump_shotgun` or `bolt_sniper` plus any changes to receiver, barrels, handguard, magazine, stock and sights), so every gun has its own silhouette, and it picks its own unlock level with `unlockLevel`. **[Weapons/README.md](Weapons/README.md)** and **[Attachments/README.md](Attachments/README.md)** are the guides, with an example and every field. A broken file is skipped with a message in the lobby, and `CallOfSlop.exe --check-weapon FILE` checks one without starting the game. Weapon XP and loadouts are saved by weapon name. In multiplayer everyone needs identical `Weapons` and `Attachments` folders: a client whose set differs from the host's is told which folder differs and disconnected.
The server checks everything: you can only fire weapons you own, at their real fire rate and spread, and you can't move faster than your fastest weapon allows.

### Sound

Every weapon has its own **gunshot, reload and equip sound**, synthesised when the game starts from the weapon's stats: calibre gives depth and thump, there's crack and echo, suppressed guns go *thwip*, and energy, pneumatic and toy guns get their own recipes. Reloads follow the weapon type (mag swap, shell-by-shell plus pump, bolt, belt box). There are also hit, headshot, kill, hurt, death and respawn cues. Other players' shots and reloads are positioned in **3D** with OpenAL, so you can hear where people are.

- **Hits.** A body hit gives the shooter a dull flesh *thwack* with a short tick; a **headshot** is a meaty crunch (a bone-crack transient, a wet low thump, a squelch and a gritty tail) under a bright hitmarker ping, and a headshot kill adds a bell-and-boom accent on top of the kill sound. Hitting a turret or drone pings metal. Hitting a **teammate** (team modes and Zombies co-op, where it does no damage) gives a muted low *bonk* with a soft falling double beep instead, quieter than an enemy hit. Everyone else hears flesh hits and headshot crunches **in the world**, quieter, where they happened. Every hit sound has three variations, picked at random.
- **Footsteps** for every player, local, remote and bots. They follow the leg animation's gait (the same cadence: about 7 steps a second at walking speed, 10 at a sprint, and locked onto the drawn legs' foot plants when they're on screen), so a step sounds as a foot lands. Sprinting is loud and heavy and carries about 45 m, walking is soft (24 m), crouch-walking is nearly silent (6 m) and prone crawling is a cloth-and-grit scuffle. Nothing in the air; a jump has a push-off, and a landing thuds harder the faster you fell. The surface comes from what's under the foot: the terrain's painted material (grass, dirt, sand, rock where it's drawn as cliff, and snow on pale ground), a box's texture (concrete, brick and tiles sound like concrete; wood; metal; earthy and grassy hills), a prop's parts (a wooden crate is wood, an oil drum is metal) and movers (metal). Each surface has 4 walk and 4 run variations, played with pitch and volume jitter. Other players' steps are positional (important information: you can hear someone sprinting round the corner); your own are quieter. Footsteps have their own **12 voices**, so 32 running players can't take the voices the gunfire needs: a new step takes a free voice, else the farthest one if it is nearer, else it is dropped.
- **Distance.** A gunshot crossfades into a **distant layer** (a muffled blast, low boom and long rolling tail) from 25 m to 90 m. Bullets passing within 3 m of you **whizz**, or **crack** for rifles, DMRs, snipers and LMGs. Shots hitting the world make an **impact** by surface (concrete chips, a metal ping, a wood thunk, a dirt puff), sometimes with a **ricochet** off metal, concrete and rock. Breaking props have three variations each.
- **OpenAL EFX** (part of the OpenAL binding) is used when the device passes a start-up canary: positional sounds are low-passed with distance (far gunfire loses its crack), and every sound gets a light reverb send that suits the map: open terrain, urban streets, or a stony room for maps that are mostly roofed over. Without EFX everything plays dry, as before. The log's `[audio]` lines give the number of sounds, synthesis time, EFX and HRTF status, and once a minute in a match the sounds played, footsteps dropped by the voice cap and CPU per sound.
- **Settings > Audio**: **Effects volume** (everything but music) and **Footstep volume** (on top of the effects volume), both default 1.00, saved as `effects_volume` and `footstep_volume` in `settings.txt`; older settings files load with the defaults. Each slider has a Test button.

### Music

The menus and lobbies have **hard electronic background music, generated live in code** (no audio files, nothing licensed): a step sequencer drives synthesised kicks, claps, hats, distorted basses, supersaw and square leads, pads, arps, risers and crashes through each track's arrangement (intro, build with snare roll and riser, drop, breakdown, second build and drop, outro). Every track has its own genre recipe, tempo, key, chord loop, riffs and arrangement:

| Where | Track | Style | Tempo | Key |
|---|---|---|---|---|
| Main menu | Slop Protocol | Hardstyle | 150 | F# harmonic minor |
| Main menu | Grinder | Industrial | 140 | D phrygian |
| Main menu | Lead Rain | Drum & bass | 174 | E minor |
| Main menu | Kill Feed Overdrive | Darksynth | 160 | C# phrygian dominant |
| Main menu | Frag Frenzy | Hardcore (gabber) | 172 | A harmonic minor |
| Lobby | Ready Up | Hardcore (gabber) | 165 | G minor |
| Lobby | Warmup Riot | Industrial | 145 | B phrygian |
| Lobby | Respawn Engine | Drum & bass | 172 | D# dorian |
| Lobby | Results Screen of Death | Darksynth | 155 | A# harmonic minor |
| Main menu | Tracksuit Tactical | Hardbass (supersaw anthem, HEY! chants) | 156 | B minor |
| Main menu | Ushanka Uprising | Hardbass (hoover, OI! chants, reverse bass in the later drops) | 146 | D harmonic minor |
| Main menu | Panelka Pressure | Hardbass (screech lead, double-time bounce, reverse bass every other 4 bars) | 161 | F phrygian |
| Main menu | Squat Speedrun | Uptempo hardbass (hoover, then supersaw; half-time breakdown) | 200 | C# minor |
| Lobby | Hey Hey Headshot | Hardbass (square rave stabs, call-and-response HEY! chants) | 152 | G# minor |
| Lobby | Squat Before Respawn | Hardbass (supersaw, then screech; reverse bass every other 4 bars, HA! chants) | 158 | C harmonic minor |
| Lobby | Kvass Overflow | Hardbass (hoover, then rave stabs; YAH! chants, reverse bass in the later drops) | 164 | E phrygian dominant |
| Main menu | Babushka Bunker Buster | Harder hardbass (9-saw supersaw, then hoover; HEY! on every beat, reverse bass every other 4 bars) | 168 | F# phrygian |
| Main menu | Kettlebell Killstreak | Harder hardbass (hoover, then screech; galloping gabber kick, half-time breakdown) | 175 | D# phrygian dominant |
| Main menu | Marshrutka Max Overdrive | Harder uptempo hardbass (supersaw, then rave stabs; the fastest track, half-time breakdowns) | 215 | A phrygian |
| Lobby | Semechki Shrapnel | Harder hardbass (screech, then supersaw; rolling double bass, HA! chants) | 171 | A# minor |
| Lobby | Dvor Siren Drill | Harder hardbass (rave stabs, then hoover; the loudest sirens, the biggest crowd) | 163 | G harmonic minor |
| Main menu | Seven Eighths of a Squat | Weird hardbass (drops in 7/8, a 5/4 build, a music-box swerve, 8-bit arps, squeaky toys, chipmunk crushed HEY!s) | 151.5 | A dorian |
| Main menu | Quarter-Tone Gopnik | Weird hardbass (quarter-tone scale, a deliberately out-of-tune harmony, 5/4 drops with a boing on the extra beat, demon/chipmunk crushed HA!s) | 147.5 | D quarter-flat minor |
| Lobby | Babushka Polka Overdrive | Weird hardbass (polka accordion lead, oom-pah bass, builds that speed up, an accelerating last drop, boings and slide whistles) | 157.5 | Bb hungarian minor |
| Lobby | Drunk Duck Breakdown | Weird hardbass (a drunk half-time breakdown that slows to 0.55x with tape wobble and sagging pitch, then speeds back up; a lo-fi swerve; rubber duck quacks) | 149.5 | F# minor |
| Lobby | Lullaby for a Tracksuit | Weird hardbass (a music-box interlude that winds down, an 8-bit chip lead in the later drops, a 5/4 build into a 7/8 drop, an accelerating finale, quacks and squeaks) | 155.5 | G melodic minor |

The **hardbass** tracks are the hardest in the set: a short, hard-clipped kick with a punchy click, the bouncy off-beat bass (each note drops onto its pitch, pumped harder by the kick's sidechain than the leads), detuned supersaw, swooping hoover, screaming band-passed screech or square rave-stab leads, synthesised crowd chants (HEY! / OI! / HA! / YAH! from formant filters, no samples), hardstyle reverse bass swells between the kicks in some drops, machine-gun kick fills, and the whole mix driven into a look-ahead master limiter so they're loud but never clip. Kick and bass lengths follow the tempo, so even the 200 BPM track stays tight.

The five **harder hardbass** tracks go further: a layered gabber kick (a second, higher kick summed under heavier drive and hard clipping, held at full scale for longer) with its own pattern every other eight bars of a drop, nine-voice supersaws with wider detune (and five voices per harmony note), a growling harder-driven off-beat bass, a second crowd an octave below every chant, sirens wailing over the builds and the first two bars of each drop, a pitch riser climbing three octaves into a dead-silent gap, kick rolls doubling to 8ths and then 16ths into the drop, and a deeper sub hit with the whole crowd shouting on the drop itself. They're mastered to the same loudness as the other hardbass tracks and cost at most about 1.5x the CPU of the heaviest older track.

The five **weird hardbass** tracks keep that core (pumping kick, off-beat bass, chants) and then misbehave: bars of 7/8 and 5/4, tempo ramps, a drunk breakdown with a tape-deck wobble, sudden swerves into a few bars of dusty lo-fi or a music box, a quarter-tone scale, a polka accordion, an 8-bit sound chip, bitcrushed and pitch-shifted chants, and toy sound effects (rubber duck quacks for the game's ducks, a squeaky toy, slide whistles, a boing), all synthesised too. They're in the Hardbass only list as well, and cost about the same CPU as the other hardbass tracks. The kill-frenzy layer only picks the ones whose drops stay in steady 4/4 (Drunk Duck Breakdown), since it loops drops on the bar line.

- **Menu tracks** play on the main menu and its pages; **lobby tracks** play in a session while no match is live: warmup / ready-up, the countdown, and the results screen with the map vote. When a match goes live the music **fades out**, and it comes back on the results screen.
- Each list is shuffled (every track once per round, never the same track twice in a row), and tracks **crossfade** into each other, as do menu and lobby music.
- The main menu shows a subtle *Now playing* line in the bottom-left corner.
- **Settings > Audio > Music volume** (0 = off, default 0.50) sits on top of the master volume. It's saved as `music_volume` in `settings.txt`; older settings files without it load with the default.
- **Settings > Audio > Music style**: **Mixed** (default, every track) or **Hardbass only** (only the hardbass tracks, in both the menu and the lobby; switching it on crossfades straight into one). Saved as `music_style` in `settings.txt`; older files load as Mixed.
- **Kill frenzy music** (in matches): as you chain kills, a hardbass track fades in and gets harder the more kills you get and the faster you get them, then fades out after a while without kills. Each kill adds *heat* (6, plus up to 14 for a follow-up within 2 s, tapering to nothing after 7 s; +3 per link of a multi-kill chain of kills under 4 s apart, up to +12; +3 for a headshot; max 400). Heat bleeds slowly (4%/s) for 5 s after the last kill, then drains (7%/s plus 3/s). Heat 12 / 30 / 52 / 85 / 200 / 330 reaches **level 1-6** (left again below 8 / 24 / 44 / 74 / 185 / 310):
  - **Level 1**: only the drums and bass, behind a closed low-pass filter (380 Hz) and quiet: the club through the wall.
  - **Level 2**: the filter opens: the full beat, claps, hats and the bouncing off-beat bass.
  - **Level 3**: the leads, harmonies, pads and chants come in.
  - **Level 4**: full volume, and the track jumps to its **last drop** (the later lead sound, reverse bass, crash and sub drop).
  - **Level 5 - FRENZY**: louder: the frenzy normally sits at 0.75 of the menu music's level, here it's as loud as the menu music (1.33x level 4), and your gunfire ducks it only half as much (15% instead of 30%). A green **FRENZY** banner (the perk-banner style) shows when it's reached.
  - **Level 6 - MAX FRENZY**: as loud as everything else in the game, if not slightly louder: with the sliders at their defaults the music's drop comes out at least as loud as (about 1.05-1.1x) a second of your own gun firing with a frag explosion on top (measured from the game's own synthesised sounds: the median over the 42 real firearms is 0.55 RMS at effects volume 1; gunfire alone 0.35, the explosion 0.42), the gunfire doesn't duck it at all, and it lands with a crash and a crowd roar plus a **MAX FRENZY** banner. The music bus goes through a look-ahead limiter (ceiling 0.96) while the frenzy plays: at level 6 it's already working at the default sliders (the music is pushed 1.2x above the reference into it), and nothing clips even with the music and frenzy sliders at 1. Note that at this level the music is as loud as your gunfire by design; the music and frenzy volume sliders still scale it down.
  - A quick double kill starts level 1, a triple kill level 3, five quick kills level 4. Level 5 needs a real spree: kills a second apart get there on the 9th kill (half a second apart: the 8th), a kill every 2 s on the 10th, every 3 s only on the 14th. Level 6 needs an exceptional one: the 15th kill a second apart (half a second apart: the 13th), a kill every 2 s only gets there on the 22nd, and every 2.5 s or slower never does. A kill every 6 s never passes level 2, every 7 s or slower never starts it. After a burst to level 4, it holds about 5.5 s, then eases down (level 2 at about 11 s) and is gone after about 18-20 s; after a maxed-out spree level 6 holds about 5.7 s, level 5 until 12 s, level 4 until 21 s, and it's gone after about 33 s.
  - Changes land on the **bar line** and crossfade: up within half a bar, down one level at a time (at most every two bars, over two bars), levels 6 and 5 included. Drops loop, so it never falls into a breakdown. **Dying** fades it out over 2 s with the filter closing, and it never plays in the death cam, menus or lobby. Your own gunfire ducks it a little (levels 1-4), and up to level 4 it sits under the menu music's level so the game stays on top.
  - **Zombies** too: zombie kills are worth half and the hold is 3.5 s (being downed counts as dying); the same six levels apply, so a horde of 30 fast kills reaches MAX FRENZY.
  - Only the local player's kills count, client-side (nothing on the network). One hardbass track from the pool per frenzy, rendered by the same engine with stem gains (drums, bass, melodic parts): about the cost of one hardbass track at levels 3-6 (5 and 6 only change gains, plus the roar for two seconds) and less at levels 1-2 (the leads aren't rendered).
  - **Settings > Audio > Kill frenzy music** (On by default) and **Frenzy volume** (0.80, on top of the music volume), saved as `frenzy_music` and `frenzy_volume`; older settings files load with the defaults. The self-test section **Frenzy music** checks the heat model (the kill patterns reaching each level), bar-synced changes, the easing down, death and the setting off, the volume per level (level 6 against the measured gunfire-and-explosion reference), the ducking per level, the limiter at the loudest sliders, the sliders still multiplying, determinism, allocations and CPU (at most 1.5x the heaviest hardbass track; level 6 no dearer than level 4).
- The music is rendered on its own background thread into a small queue of OpenAL streaming buffers (about 0.2 s), without allocating; it costs about 1% of one CPU core (the self-test measures it).

### Zombies (in development)

A World at War style **Zombies** mode (co-op for 1-4, new maps) is being built. Milestone 1, the **modular weapon builder**, is playable now as a sandbox: main menu > **WORKBENCH (ZOMBIES PREVIEW)**.

- A weapon is a tree of **parts** (123 so far, in `Parts/`): the **grip** is the root and holds one or more **receivers** (a pistol grip 3, a rifle grip 9, up to 16 on the late-game frames and the push cart), and each receiver has its own barrels, **splitters**, feed, ammo, a **damage multiplier** slot and two **receiver upgrade** slots (behind and beside it). Stocks, optics, foregrips, a **shared feed** (one hopper feeding several receivers) and utility parts go on the grip.
- The **grip fixes the fire style**: volley grips fire every receiver at once, rotate grips take turns. **Splitters never change the total damage** (rarer ones are more accurate or have more outputs); damage comes from multipliers and upgrades. **Weight** is the limit: rarer frames and weight-relief parts (slings, harnesses, an anti-grav coil, helium balloons) carry more, so huge guns come late in a run.
- The **push cart** is a real cart: you push it along the ground with both hands on the bar (it can't climb steps or steep slopes, and it stops at walls), and it doesn't fire while you push it. **E parks it**; **tap E** at its bar to **man its gun** (you stand at the bar, the mouse turns the gun up to 60 degrees either side, 15 down and 30 up, and the fire key fires the cart's build); **E** (or jump / sprint) steps off back to your other weapon, and **holding E** at the cart pushes it again. (An auto-firing turret may come later as a separate deployable.)
- The **melee slot**: build a melee weapon from a hilt, a head and mods (machete, nail bat, sledgehammer, fire axe, chainsaw sword, a frying pan, a frozen fish...) with its own damage, swing speed, reach, cleave and stun. It replaces the knife only in the workbench range (and Zombies later); normal matches keep the standard melee.
- The workbench shows every part (all unlocked in the sandbox), a live 3D preview, the receiver slots, stat changes before you fit a part, and a plain reason when something doesn't fit. Four build slots and the melee slot are saved in `workbench.txt`; the **standard builds** (`Parts/Builds/`, what the mystery box and wall weapons will give) load as presets. Sandbox toggles: weapon slots 3-4 (in Zombies an upgrade bought on the map, per run; the range also has its station) and practice dummies. **Test fire** opens the **Workbench Range**.
- New keys: **E** interact (cart, slot station) and **3** for weapon slot 3 (slot 4 is unbound by default); the swap key and mouse wheel cycle through every slot you have.
- Every barrel fires from its own muzzle, minigun receivers spin up, and one trigger pull counts as one hit per target (a Last Stand shield loses one charge, however many barrels hit). The standard weapons are unchanged.
- The design (component model, rules, stat pipeline, the user's decisions, rarity tiers, run progression and drop tables, the cart, the melee slot, example builds and the open questions) is in [docs/zombies/WEAPON-BUILDER.md](docs/zombies/WEAPON-BUILDER.md).

**The Zombies mode (milestone 2)** is playable on the dev map **Zombies Test**: pick that map, then the game mode **Zombies** (single player with bots, or host for up to 4).

- **Rounds**: a handful of slow zombies in round 1, more and tougher every round (health +100 a round to round 9, then +10 percent), runners from round 3 and sprinters from round 5; at most 24 alive at once. A 10 s break between rounds. The round is the red tally bottom-left.
- **Zombies** come through **boarded windows**: they tear the boards off, climb in and chase the nearest player standing, through open doors. **Hold E** at a window to put boards back (10 points each).
- **Points** (personal): 10 a hit, 60 a kill, 100 a headshot kill, 130 a melee kill. Spend them on **doors and debris** (unlock the next area and its spawners), **wall guns** (chalk outlines; ammo if you already have it), the **mystery box** (a complete build; the teddy bear moves it), **perk machines** (Juggernog, Speed Cola, Double Tap, Quick Revive, Second Wind), the **weapon-slot upgrade** (2 -> 4 slots) and the **power switch** (some buyables need it). Hold E to buy; the prompt shows the price.
- It's a **roguelite run**: you start with the Wall Rifle, the Wall Pistol and a Machete; zombies drop **parts**, salvage piles give **scrap**, and a **workbench** (E) lets you rebuild your guns with the parts you own this run, break parts down and craft new ones.
- **Downed** at 0 health: a teammate revives you by holding E on you; you lose your perks; after 30 s you bleed out and come back next round with the start gear, and your **built weapons drop where you went down**: anyone can pick them up (hold E) for 5 minutes. **Second Wind** (start room) gets you back up on your own once, solo or co-op. Guns have **reserve ammo** in Zombies (magazine | reserve).
- Co-op: built guns and dropped builds replicate to every player (protocol 19). The design, every number and the map format are in [docs/zombies/ZOMBIES-MODE.md](docs/zombies/ZOMBIES-MODE.md).

## Development and version control

The repository holds only the source: `Source/`, `Maps/`, `Weapons/`, `Attachments/`, `tools/`, the project file and the docs. Build output (`bin/`, `obj/`) is ignored. So are your personal game files (`progress.txt`, `settings.txt`, `loadout.txt` and logs), which live next to the built game in `bin\Release\net10.0\` and are never committed.

- **Get started:** clone the repo, then run `Start.bat` (or `dotnet build -c Release`). NuGet fetches the BRUTAL packages on the first build.
- **Automatic checks:** with `.github/workflows/build.yml` in the repo, every push runs **Build and self-test** on GitHub Actions. It builds on Windows, runs `CallOfSlop.exe --selftest` and attaches the logs to the run. A red X on a commit means it didn't build or a check failed; open the run to see which.
- **Line endings:** `.gitattributes` keeps text files LF in the repo and batch files CRLF, so diffs stay clean whichever editor or OS touched a file.

## Testing without playing

- `tools\BuildCheck.bat` builds the game, then runs the automatic checks and writes `bin\buildcheck.done` when finished (the build output is in `bin\build.log`). The window smoke test only runs if a file called `tools\run_smoke.flag` exists.
  - **`CallOfSlop.exe --selftest`** runs headless (no window). It checks every map: spawns, waypoints, and a 90-second offline match with 7 bots looking for crashes, stuck bots and slow ticks (no server tick may do more than 8 ms of work), a 0-bot test-mode match that must still be running after a simulated minute (and every other mode alone), the pause menu's Kill yourself (offline and through the server handler, with its rate limit), then checks the bots: a nav graph and A\* routes on every map, a bot walking to a far goal, the headshot share of bot kills and that personalities fight at different ranges, and **collision parity**: the physics world's ray casts, body and point checks and the classic movement give bit-identical answers to the old collision code (every-box scans and the old collision grid) on every map (movement on maps without terrain: terrain follows the slope rules since protocol 14), also with breakable props broken. The **Slopes** section checks the terrain slope rules in both movers: uphill speed against slope angle at walk and sprint, no airborne ticks over a crest or down a 30 degree slope, no stopping or sliding on a noisy but walkable 36 degree slope, steep slopes sliding and cliffs blocking, the slide cap and steering, a 0.4 m terrain lip stepped over and a 0.9 m bank needing a mantle, exact replays, a scripted walker along the bots' routes from both team spawns to every hill and the enemy flag on each High Ground map, and a 60-second bot match on each of them with no stuck bots (its numbers also appear as notices on the CI run). The **Physics** section checks the BVH against brute force on every map (rays with normals, box and capsule overlaps, sphere casts), oriented-box and mesh colliders, determinism (two worlds, a shuffled world, 32 bodies x 600 ticks twice, replays from mid-stream state), the swept mover (steps, stairs, 30-50 degree ramps, a 5 cm wall at sprint speed, a ceiling, mantling, terrain, movers, short bot matches) and costs. It also round-trips every map through the map file format, checks that the example in the map guide loads and that broken files give useful errors, does the same for weapon and attachment files (and checks old saves migrate), checks mantling through a window and onto a crate, the High Ground pack (two maps of each size, all on terrain with real relief, hand-placed spawns for each map's player limit, objectives for every mode on level ground, a nav graph that reaches them all, and props on every map: none in the way of a spawn, hill zone or flag, at most two explosive barrels well away from the spawns, and every prop mirrored by a twin for the other team), terrain (heights matching the map tools, ray casts, walking up slopes and into cliffs, grenades resting on slopes, ops and spawns placed on the ground, a bot match on the terrain demo), **terrain holes and caves** (the hole mask from `hole` features, ground, ray and body queries through holes, falling in and grenades dropping in and bouncing off pit walls and cave ceilings, no plants or clutter over holes, no surface triangles over holes, walking in at one Cave Test entrance and out of the other, a bot route out of the sinkhole through the tunnel, pits bots can't leave never chosen as goals, every other map's fingerprint unchanged and no slowdown on terrain without holes), moving platforms (the same pose for the same tick on host and client, riders carried by every kind of mover, pushed rather than passed through, momentum kept when jumping off, ray casts on a turned platform, exact replays for prediction, a nav graph that keeps out of their paths and a bot match on Physics Test), every attachment on every weapon, the arms (IK accuracy, and every weapon through every animation: no NaNs, hands on the gun, sights still aligned), every optic's sight line and fit (nothing on the gun clashes with it), **sight pictures** (how much of the screen each lens covers at full aim: 1x windows unchanged, more with magnification, 8x and up at least 90%; no part of the gun between the eye and the lens on any weapon; the lens growing without a pop from the hip to full aim; the steadied lens under recoil; optic files' `sight` blocks), and snapshot packet sizes. **Big lobbies:** every map's player limit and spawns (generated spawns free, deterministic and leaving the fingerprint alone), the lobby slider's cap and random map choice, 32-player snapshots (exact round trip within the quantisation, splitting, every packet under 1200 bytes), bots leaving a smaller map and coming back, join refusal when a map is full of people, real loopback games at 8 / 16 / 32 players (the client sees everyone with names, scores and positions; bandwidth reported) and 32-player and 16-player bot matches within the 8 ms tick budget. **Props:** every prop file loads and validates, solid props block rays and movement and cosmetic ones don't, Clutter is deterministic, keeps clear of spawns, routes and objectives and mirrors its props when asked, nav graphs and bots on the prop maps, and breakable props (each kind of damage, the break event and broken set over the network, collision gone once broken, debris capped and despawning, a new match restores them). **Animated props:** broken `anim` blocks give readable errors, every animated prop loads with sane pivots and axes, the GPU records and per-vertex stream add up, animation is a pure function of time and position (deterministic, a different phase per copy, frozen beyond its distance), colliders and fingerprints don't change, the starter props' meshes and the Prop Gallery's instance buffer hash as before, and the Animated Props map loads and plays a bot match. **Meshes:** every procedural shape (vertex and triangle counts, closed and consistently wound, unit normals facing out, volume and bounds against the formulas, LODs, the same bytes every build), meshoptimizer (cache optimisation keeps the shape, simplification, and the fallback without it), the glTF test models (node tree and order, a node matrix and TRS, materials back in sRGB, the embedded texture, collision soups), 17 broken or missing model files each rejected with a readable message, MeshPool packing, the Mesh Test map (round trip, cosmetic: no boxes or fingerprint change) and readable errors for bad Mesh and Model ops, plus a canary for every BRUTAL native the mesh pipeline uses (meshoptimizer, stb through Brutal.Texture) with the result in the log. **Scene graph:** transform maths (parent and child, dirty subtrees only, reparenting that keeps the world transform), sockets and lookups, the glTF builder (lamp_post.gltf's node tree against the loader), gravity frames, vehicle seats and mover riders, then every first-person weapon setup through hip, aim, reload and firing, third-person players in every stance, every mover and the turrets and drones drawn both through the scene graph and with the old code and compared draw by draw (matrices within 1e-5), no allocations per frame, and 10,000 nodes updated and flattened within 0.3 ms. It also renders every music track start to finish headlessly (no NaNs, no heavy clipping, drops louder than breakdowns, tracks distinct from each other, the same output for the same seed, no allocations, CPU cost within budget for every track), checks the hardbass tracks (tempo rules, loudness window, the weird tracks' meters, tempo maps, swerves, toys and new voices, CPU against the heaviest normal track, never past the limiter's ceiling, harder than the other tracks, kick and off-beat bass that don't smear, the chants, reverse bass, clipped kick and master limiter on their own, the Music style setting), the harder hardbass tracks (tempos, 90-180 s lengths, loudness in line with the other hardbass tracks, heavier kicks, CPU within 1.5x the heaviest older track as a median on the same machine, the siren, pitch riser and crowd voices on their own), checks the menu / lobby / live-match music switching, shuffling and crossfades, and loads the music volume from old and new settings files (temporary files only). The **Sound** section synthesises every sound (non-silent, no NaN, no clipping, variations really differ), checks the headshot crunch against a body hit (longer gritty tail, different brightness) and the hit, kill and accent sounds against each other, footstep surfaces on test geometry (a concrete floor and block, a metal container, a wooden box and crate prop, a Physics Test mover, grass and rock on Ridgeline and snow on Alpine Pass), footstep cadence against the gait at walk, sprint, crouch and prone, no steps in the air, one push-off per jump and one landing per fall (harder from higher), the 12-voice footstep cap with 32 sprinting players (the nearest always heard), the reverb class of every map, the effects and footstep volume settings, and the OpenAL start-up with the EFX canary when there's an audio device (skipped without one). The **Score scaling** section checks the score limit formula for every mode (the classic limit at 8 players, never lower for more players, rounding, Fixed), that a match's limit is set at the start, only rises when people join mid-match and replicates, the `score_limit` setting (old files get Auto), and that free-for-all and TDM bot matches at 16 players reach their limit within 35% of the time 8-player ones take. It also round-trips every map through the map file format, checks that the example in the map guide loads and that broken files give useful errors, does the same for weapon and attachment files (and checks old saves migrate), checks mantling through a window and onto a crate, the High Ground pack (two maps of each size, all on terrain with real relief, hand-placed spawns for each map's player limit, objectives for every mode on level ground, a nav graph that reaches them all, and props on every map: none in the way of a spawn, hill zone or flag, at most two explosive barrels well away from the spawns, and every prop mirrored by a twin for the other team), terrain (heights matching the map tools, ray casts, walking up slopes and into cliffs, grenades resting on slopes, ops and spawns placed on the ground, a bot match on the terrain demo), moving platforms (the same pose for the same tick on host and client, riders carried by every kind of mover, pushed rather than passed through, momentum kept when jumping off, ray casts on a turned platform, exact replays for prediction, a nav graph that keeps out of their paths and a bot match on Physics Test), every attachment on every weapon, the arms (IK accuracy, and every weapon through every animation: no NaNs, hands on the gun, sights still aligned), every optic's sight line and fit (nothing on the gun clashes with it), **sight pictures** (how much of the screen each lens covers at full aim: 1x windows unchanged, more with magnification, 8x and up at least 90%; no part of the gun between the eye and the lens on any weapon; the lens growing without a pop from the hip to full aim; the steadied lens under recoil; optic files' `sight` blocks), and snapshot packet sizes. **Flag pool:** every flag design loads with its aspect and stays inside its flag, broken flag files give readable errors, each flag prop's variants keep its colliders and stay within a vertex budget, stripes move with the cloth in the wind, random picks are deterministic, roughly uniform and mirror-symmetric, placement overrides and a map's `"flagPool"` list work and survive a save, flags never change a fingerprint, and the older packs fly none. It also renders every music track start to finish headlessly (no NaNs, no heavy clipping, drops louder than breakdowns, tracks distinct from each other, the same output for the same seed, no allocations, CPU cost within budget for every track), checks the hardbass tracks (tempo rules, loudness window, never past the limiter's ceiling, harder than the other tracks, kick and off-beat bass that don't smear, the chants, reverse bass, clipped kick and master limiter on their own, the Music style setting), checks the menu / lobby / live-match music switching, shuffling and crossfades, and loads the music volume from old and new settings files (temporary files only). Results go to `bin\Release\net10.0\selftest.log`.
  - **`CallOfSlop.exe --smoketest`** opens the real window and steps through the lobby, the loadout and gunsmith screens, the respawn flow, full ADS through a red dot, a 3x, a 4x, an 8x and a 12x (`smoke_ads_*.bmp`, each with a `[smoke] ads_*` line giving how much of the screen the lens covers, plus `[optics]` lines with the lens pass's GPU time next to the world pass's), the arms during a reload, a sprint and a melee (`smoke_arms_*.bmp`), the death cam (a bot kills you; `smoke_deathcam.bmp`) and a few seconds of play on eight maps (the Dev pack's Terrain Test, `smoke_play_terrain.bmp`, with a `[smoke] terrain:` line giving chunks, draw calls and triangles, then High Ground's Adobe Mesa, `smoke_play_highground.bmp`). Before the maps it rides the Physics Test's slow disc and elevator for a few seconds each (`[smoke] movers` lines with position, speed and fps, `smoke_movers_*.bmp`), then (after the foliage views) looks at the Prop Gallery, Prop Gallery 2 and 3 (`smoke_props_gallery2.bmp`, `smoke_props_gallery3.bmp`) and Prop Yard, breaking the props in view once (`[smoke] props` lines with fps, props drawn, draw calls and GPU time, `smoke_props_*.bmp`), the Animated Props map (`smoke_props_animated_a.bmp` and `_b.bmp` a quarter of a second apart, so the motion shows, with the animated pass's GPU time in the `[smoke] props` line, then `smoke_props_animated_overview.bmp`), and the Cluttered pack's Carousel from above (`smoke_cluttered_small.bmp`) and Sprawl from above and in its market (`smoke_cluttered_large*.bmp`, with `[smoke] foliage` lines adding plants drawn, foliage draw calls and the box count), and the Mesh Test map from four views and the Shape Test bean from two (`[smoke] mesh` lines with fps, parts drawn, draw calls, LODs and GPU time, `smoke_mesh_*.bmp`, `smoke_mesh_shape_*.bmp`), then a stand-in player walking up the Physics Test stairs, sprinting, and its own legs seen looking down (`[smoke] legs` lines, `smoke_legs_walk.bmp`, `smoke_legs_run.bmp`, `smoke_legs_firstperson.bmp`), and after the sky views the Cave Test map from outside its west entrance and inside its chamber (`[smoke] cave` lines with fps, terrain draw calls and triangles, `smoke_cave_entrance.bmp`, `smoke_cave_chamber.bmp`). It logs fps and GL errors to `callofslop.log`, then quits. Before the maps it rides the Physics Test's slow disc and elevator for a few seconds each (`[smoke] movers` lines with position, speed and fps, `smoke_movers_*.bmp`), then (after the foliage views) looks at the Prop Gallery, Prop Gallery 2 and 3 (`smoke_props_gallery2.bmp`, `smoke_props_gallery3.bmp`) and Prop Yard, breaking the props in view once (`[smoke] props` lines with fps, props drawn, draw calls and GPU time, `smoke_props_*.bmp`), the Animated Props map (`smoke_props_animated_a.bmp` and `_b.bmp` a quarter of a second apart, so the motion shows, with the animated pass's GPU time in the `[smoke] props` line, then `smoke_props_animated_overview.bmp`), the Flag Lineup's four flags (`smoke_props_flags.bmp`), and the Cluttered pack's Carousel from above (`smoke_cluttered_small.bmp`) and Sprawl from above and in its market (`smoke_cluttered_large*.bmp`, with `[smoke] foliage` lines adding plants drawn, foliage draw calls and the box count), and the Mesh Test map from four views and the Shape Test bean from two (`[smoke] mesh` lines with fps, parts drawn, draw calls, LODs and GPU time, `smoke_mesh_*.bmp`, `smoke_mesh_shape_*.bmp`), then a stand-in player walking up the Physics Test stairs, sprinting, and its own legs seen looking down (`[smoke] legs` lines, `smoke_legs_walk.bmp`, `smoke_legs_run.bmp`, `smoke_legs_firstperson.bmp`). It never saves your settings or progress.

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
| 32 players in one packet | **Compact snapshots.** Positions are sent to 1/256 m and angles to 0.1 mrad (24 bytes a player); your own state comes exact, so prediction never chases rounding. Scores, loadouts and perks go out only when they change (repeated for half a second) plus a slow refresh. Players more than 40 m away, and the dead, update at 15 Hz instead of 30 (deaths and respawns still go out at once). A snapshot that still doesn't fit 1200 bytes is split into self-contained parts. |
| Standing on a moving platform | **Stamped mover clock.** A platform's pose is a pure function of the server tick. Each input you send carries the tick the server will run it at (your client steers this from the snapshots), and the server runs it at exactly that tick, so prediction, replays and the server all see the platform in the same place. Other players on platforms are drawn riding them, and shots at them are rewound with the platform too. |

Movement is now server-authoritative, so clients can't teleport. Shots are still resolved only on the server.
Hit markers appear instantly from your own prediction, and the server confirms them. Hitting a teammate (bullets, melee or a grenade; never in Free-for-all) shows a cyan "no entry" sign with FRIENDLY under the crosshair instead of the red/yellow marker, since teammates take no damage.

**Testing bad connections on one PC:** add `--lag=150` (extra milliseconds on everything that side sends, plus some jitter) and/or `--loss=5` (percent of packets dropped):

```sh
Start.bat --lag=150 --loss=3
```

On clients, the window title shows ping, the current interpolation delay, and the size of the last prediction correction ("fix ... cm").

**Physics**

All collision goes through one module, `Source/Physics`:
- `PhysicsWorld` is the current map's collision world (`World.Physics`): one BVH (`Bvh.cs`, surface-area-heuristic build) over every static collider (map boxes, prop collider boxes, oriented boxes, triangle meshes via `IMeshCollider`, e.g. `TriangleMeshCollider.FromMesh` from the mesh pipeline), plus the terrain heightfield and the movers posed at a tick. Broken props switch their boxes off.
- `IWorldQuery` (`World.Query`) is the one query interface: `Raycast` (distance, or the closest hit with point, normal and shape), `SphereCast`, `CapsuleCast`, `OverlapBox`, `OverlapCapsule`, `OverlapBody` (the classic player box), `PointInside`, all with a `QueryFilter` (layers: boxes, props, terrain, movers, mover sweeps, oriented boxes, meshes; the mover tick; shapes to ignore). Bullets, lag-compensated shots, line of sight, grenades, explosions, bots and their nav graph, mantling, kill streaks, props and movers all use it.
- **Movement** goes through `World.Mover` (`IMover`). `ClassicMover` is the original movement, unchanged on maps without terrain (the self-test proves bit-identical results); on terrain both movers follow `SlopeRules` (protocol 14: the footprint's slope, 45 degree limit, slower uphill, grip and terrain steps, capped steerable slides; see [Slopes](#slopes)). `SweptMover` is a capsule character controller: sweep and slide (it can't tunnel, even at sprint speed into a 5 cm wall), step-up (0.55 m steps climb, 0.70 m block), ground snap down stairs and slopes, 42 degree slope limit on boxes and meshes (steeper ground slides you down; terrain uses `SlopeRules`), crouch and prone capsules, mantling, riding and being pushed by movers. It reads "up" and gravity from `PhysicsWorld.Gravity`, so a later gravity system can change them per zone.
- The host picks the movement with `physics=classic` (default) or `physics=swept` in `settings.txt`; it travels in the Welcome and the server info (protocol 13), and clients predict with the host's choice.
- Cost: a ray cast is about 0.5 us on the biggest map (the old box scan took about 3 us), 32 players' movement takes 15-30 us per tick classic and 15-200 us swept (the high end on terrain maps).

**Arms and gun animation**

Your gun is held by box arms (sleeves in your player colour, tan gloves). Everything is procedural, with no animation files:
- `Render/ArmAnchors.cs` knows where the hands go on each gun model (grip, handguard or pump, magazine well, bolt or slide, muzzle), including foregrips, lasers and magazine attachments, and picks the reload style: box magazine, pistol mag drop, revolver speedloader, shell-by-shell tube, break-open, bolt action, LMG box or muzzle loader.
- `Render/ViewModelAnimator.cs` moves the gun (hip and ADS, breathing sway, walk bob, sprint, jump and landing dip, equip, a rifle-butt or pistol-whip melee, the reload of each style timed to the weapon's reload time, the ledge plant while mantling, recoil kick) and works out where both hands are.
- `UI/WorldLabels.cs` (`Hud.Project`, `Hud.WorldLabel`) puts HUD text at 3D positions: the frame's real view and projection, drawn on ImGui's background list (over the world, under the HUD). The name tags (`UI/NameTags.cs`, drawn by `UI/UiNameTags.cs`) and the Zombies dropped-build labels use it.
- `Render/HeadBob.cs` is the camera head bob (see **Head bob** under Display). With it on, the gun's own walk bob follows the camera's footfalls and is smaller, so the two don't fight or double up.
- `Render/Arms.cs` solves each arm with two-bone IK (shoulder, elbow pole, wrist) as a small hierarchy: shoulder, upper arm, forearm, hand.
- `Render/ThirdPersonArms.cs` gives other players the same arms, with simple reload, sprint and melee poses from the game's reload and melee events.

The shoulders are fixed to the camera, not the screen edges, so on wide and ultrawide screens the arms come in from the bottom and aren't stretched. When you aim through a magnified optic, the off hand moves back onto the magazine so it stays out of the lens.

**Legs and locomotion**

Players have box legs (trousers in a darker shade of their colour, knee pads, boots) as scene graph nodes: pelvis -> thigh -> shin -> foot per side, posed every frame by `Render/LegAnimator.cs`. The legs are presentation only: hit boxes, collision and movement are unchanged (the torso box was shortened so torso and legs fill the old body's space).
- **Gait** from the player's drawn velocity relative to where they face (forward, backwards, strafing, diagonals), with one continuous phase: cadence rises with speed (3.7 gait cycles, i.e. 7.4 steps, per second at the 6 m/s walking speed and 5 cycles at the 13.5 m/s sprint) and the stride follows, so planted feet don't slide. The **walk** (at or below 6 m/s) has short steps, feet planted 58% of the cycle, an 11 cm foot lift, a small hip bob (high at mid-stance) and a slight lean; the **run** (sprint speed) has a 2.7 m stride, feet down only 32% of the cycle with a flight phase, a 32 cm foot lift with an early heel kick, hips 8 cm lower, more bounce (low at mid-stance) and a 0.2 rad forward lean. Between the two every parameter blends by speed (smoothstep from 6 to 13.5 m/s), so there is no pop. Steps are shorter and quicker sideways and on slopes. Planted feet lock to the spot they touched down on.
- **Crouch-walk** (lower hips, one foot back, short low steps), **prone crawl** (legs straight back, one knee drawn up and out at a time), **idle** (feet stay put; turning in place or drifting makes them shuffle back under the hips, one foot at a time), **jump** (legs tucked), **falling** (legs down, one forward), **landing** (hips dip by how hard you landed), **mantling** (lead leg up on the ledge).
- **Foot IK on the ground:** each foot casts one short ray down through `World.Query` (map boxes, props, oriented boxes, meshes, terrain and moving platforms at the drawn mover tick) for players within 40 m of the camera; farther players use the flat gait. Feet stand on stairs, slopes, terrain and platforms, tilt a little to the slope, the pelvis sinks (up to 60 cm, the upper body follows at most 25 cm so the head stays near the hit box) to keep both feet in reach, and a foot still out of reach is held up rather than stretched. Each leg is solved with the same two-bone IK as the arms (`Arms.SolveTwoBone`, knee pole forward).
- Remote players animate from their interpolated snapshot velocity (plus on-ground and mantling flags carried in the snapshots they already get), bots from their simulated velocity; the velocity is smoothed and the phase integrated, so 15/30 Hz snapshots don't make the gait jitter.
- **First person:** you see your own legs when you look down (drawn 10 cm behind your feet, without the pelvis, torso, gun or arms; nothing comes near the camera). Set the environment variable `CALLOFSLOP_FPLEGS=0` to hide them.
- Cost: about 2 us per player per frame on the CI runner including the two rays, no allocations. The self-test's **Legs** section checks the IK, that the idle legs fit the hit box, foot planting at walk, jog, run, backwards, strafe, diagonal, ADS and crouch speeds, a walk-to-sprint-and-back sweep at 240 Hz for pops (foot, hips, cadence), idle turning, jump, fall, landing and mantle poses, planted feet against an independent ground ray on a staircase (classic and swept movement, walking and sprinting) and 30 and 40 degree ramps, that hit and collision volumes are unchanged, zero allocations and the cost. The smoke test's legs step films a stand-in player walking up the Physics Test stairs and sprinting (`smoke_legs_walk.bmp`, `smoke_legs_run.bmp`, `smoke_legs_firstperson.bmp`, `[smoke] legs` lines with speed, gait, feet and cost).

**Scene graph (object hierarchy)**

Things built from parts are held as a tree of parented nodes (`Source/Scene`, see [Source/Scene/README.md](Source/Scene/README.md)): each node has a local position, rotation and scale, a cached world matrix that is only recomputed when it or a parent moved, optional named attachment points (sockets such as `muzzle`, `grip`, `optic_mount`, `hand_l`, `seat_0`) and something to draw (a box today, or a mesh from the mesh pipeline, drawn instanced). The first-person view is camera -> gun -> body, magazine, optic, muzzle device and underbarrel parts (the magazine node moves during a reload) and camera -> shoulder -> upper arm -> forearm -> hand for each arm; other players are body -> head and visor, legs (pelvis -> thigh -> shin -> foot), gun in hand and both arms; moving platforms are a frame with their boxes and a riders node; turrets are base -> head -> barrels with a muzzle socket and drones are body -> rotors and gun pod. glTF models load straight into node trees. The draws are the same as before, in the same order (the self-test compares every matrix with the old drawing code). Setting the environment variable `CALLOFSLOP_SCENEGRAPH=0` falls back to the old immediate drawing; the smoke test logs a `[smoke] scene graph:` line with nodes, draws and CPU time per frame.

**Project layout**

```
CallOfSlop.csproj   Start.bat   README.md   NOTICE.txt
Maps/                map packs (one folder each, e.g. Maps/Classic: *.json maps + pack.json, plus Props/ and Models/ for assets only that pack uses) and the map makers' guide - copied next to the game on build
Weapons/             weapon files (*.json, one subfolder per class) and the weapon guide - copied next to the game
Props/               map props (*.json, one subfolder per category) and the prop guide - copied next to the game
Models/              glTF models for the Model map op (*.glb, *.gltf) and the model guide - copied next to the game
Attachments/         attachment files (*.json, one subfolder per slot) and the attachment guide - copied next to the game
tools/               BuildCheck.bat (build + self-test), propviz (prop previews to PNG without the game), models/make_models.py (writes the test models)
Source/
  App/               Program (window, input, camera, scene + HUD drawing, smoke test), Settings, Keybinds, SelfTest
  Physics/           PhysicsWorld (BVH collision world), IWorldQuery, Bvh, Geometry + CapsuleContact (shape maths), MeshCollider,
                     ClassicMover + SweptMover (IMover), LegacyCollision (the old code, kept as the self-test's reference)
  Game/              World (rules, constants, players), Game (server/client logic, hitscan), Match, MapVote,
                     Bots (+ BotNav graph/A*, BotMove, BotTactics, BotCombat, BotPersonality, BotStats, BotObjectives),
                     Throwables, Progression, ServerAdvert, Loadouts (slots + loadout.txt), Perks, Melee,
                     KillStreaks (definitions, counting, strikes), StreakEntities (turrets, drones), BotStreaks
  Maps/              Map (geometry + building ops, loader), MapKit (bigger ready-made pieces), MapFile (JSON load/save), MapPack,
                     MapMeshes (the Mesh and Model ops)
  Props/             PropDef + PropFile (prop JSON), Props (library), MapProps (PlaceProp/Clutter ops, collision, breakable
                     state), PropMeshes + PropBatches (GPU-ready data), PropDebris (pooled debris); GameProps (in Game/) breaks them
  Net/               Net (RakNet wrapper, packets), ServerBrowser (+ master server), Upnp
  Weapons/           Weapons (WeaponDef + loader), Attachments (kits + loader), WeaponFile (weapon/attachment JSON,
                     fingerprint, --check-weapon), GunModel (optic models and lenses, per-class fallback models), Sights (sight maths, "sight" blocks), GunSpec + GunBuild (weapons built from their "model" block)
  Render/            Renderer (shader, cube mesh, boxes), Textures (procedural texture array), StreakRender (turrets, drones, strikes),
                     Arms + ArmAnchors + ViewModelAnimator + ThirdPersonArms (arms, IK and gun animation), LegAnimator (legs: gait, foot IK), SightLens (the view through optics),
                     Mesh/ (the mesh pipeline: MeshData vertex format, MeshPrimitives, MeshLibrary cache + LODs, GltfModelLoader,
                     MeshOpt (meshoptimizer), MeshTextures (imported textures), MeshCollision, MeshPool (shared buffers), MeshRenderer),
                     PlayerRender + MoverRender (the old immediate drawing, kept as the scene graph's reference and fallback)
  Scene/             the scene graph: SceneNode, Scene (dirty transforms), DrawList (flatten + submit), Renderable (box or mesh),
                     SceneBuilder (glTF-shaped node lists), SceneMeshes (mesh handles, glTF ModelNode trees), ScenePresentation (the
                     game's scene), Rigs/ (GunRig, ArmNodes, ViewModelRig, PlayerRig, MoverRig, VehicleRig, TurretRig + DroneRig)
  UI/                Lobby (menus, server browser, settings), Ui (HUD, scoreboard), UiLoadout (loadout screen), LoadoutNav (its page stack), UiGunsmith,
                     UiStreaks (streak card, picker, icons and HUD)
  Audio/             Audio (OpenAL playback + procedural sound synthesis), Audio.Efx (distance low-pass + map reverb),
                     SynthFoley (hits, footsteps, whizzes, impacts, distant gunshots), Footsteps (gait, surfaces, voice cap),
                     Music (player, crossfades, streaming thread),
                     MusicTracks (the track list), MusicRenderer (sequencer + arrangement), MusicSynth (synth voices and effects),
                     MusicTracksHarder + MusicRendererHarder (the harder hardbass tracks and their recipe),
                     MusicFrenzy (kill-frenzy heat meter + adaptive in-match layer, levels 1-6), MusicRenderer.Frenzy (stems, drop loops, jumps, crash), Audio.FrenzyReference (the gunfire-and-explosion loudness level 6 matches)
```

**Command-line options:** `--selftest`, `--smoketest`, `--lag=MS`, `--loss=PCT`, `--no-upnp`, `--master` (run a public server list), and for map makers `--check-map FILE`, `--map-ops` (list every op and its parameters) and `--export-maps DIR` (rewrite every loaded map in the standard layout), and for weapon makers `--check-weapon FILE` (check a weapon or attachment file) and `--export-weapons DIR` (write the loaded weapons and attachments back out).

All tuning values (damage, fire rate, speeds, arena size) live at the top of `World.cs`.

## Licence note

BRUTAL is free to use, but section 2 of the BRUTAL License asks shipped products to credit BRUTAL, show the BRUTAL logo before gameplay and ship `NOTICE.txt`.
This prototype prints the credit and ships `NOTICE.txt`; add the logo splash before you distribute it.
