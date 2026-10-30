using System.Diagnostics;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// What a hosted game tells the world, answered automatically by RakNet to unconnected pings
/// (the "offline ping response"). Starts with a magic tag so it can be found inside RakNet's pong.
/// </summary>
public readonly record struct ServerInfo(string Name, string MapName, byte Players, byte MaxPlayers, byte Bots, MatchPhase Phase, ushort Port, byte Version,
    GameMode Mode = GameMode.FreeForAll, PhysicsMode Physics = PhysicsMode.Classic, string Rotation = "",
    uint InstanceId = 0, string ListHost = "", ushort ListPort = 0, string Build = "", byte Gravity = 100)
{
    // (Build, the host's release version such as "0.2026.1022.5" (BuildInfo.cs; "" for dev builds and older hosts), rides
    // after the rotation as the tagged extension 'B', a length and ASCII (after 'I' and 'L', see Encode).
    // Older builds stop reading after the rotation, so it needs no version bump either; the browser uses it to say
    // "your build is older / newer" next to a game.)
    // 3: perks and melee (loadout and snapshot packets changed); 4: weapon files (weapon-set fingerprint in the Welcome);
    // 5: kill packet carries the weapon kit, killer HP and damage source; 6: kill streaks (loadout packet + streak messages);
    // 7: game modes and ready-up (mode state, ready and mode event packets, warmup phase, mode in the server info);
    // 8: per-streak stored kills in the streak state packet (using a streak empties it); 9: suicide request message;
    // 10: sprint speed 9 -> 13.5 m/s (shared movement sim);
    // 11: up to 32 players: quantised, split snapshots with a presence mask (Snapshot.cs), server-full message,
    //     per-map player limits (max players in this info is the map's), TDM kill limit in the mode state
    // 12: breakable props (prop break and prop state messages)
    // 13: physics mode (classic | swept movement) in the Welcome and at the end of this info
    // (Rotation, the host's map rotation such as "True Random", rides after the physics byte as an optional trailing string:
    // older builds stop reading before it and newer ones read "" from older hosts, so it needs no version bump.)
    // 14: terrain slope rules (SlopeRules: footprint slope, walkable to 45 degrees, slower uphill, grip and terrain steps,
    //     capped steerable slides) in both movers: client prediction must run the same movement as the server
    // 15: Last Stand shield charges and a regenerating flag in the snapshot's per-player flags byte, shield hit message
    // 16: Zombies preview: push carts (cart use, cart state messages) and per-run flags (run state message: weapon-slot upgrade)
    // 17: the push cart is a manned gun, not a turret: cart use carries tap / hold, cart state carries the gun's aim
    //     (yaw offset and pitch) instead of turret seconds, and mode 2 is "manned"
    // 18: the Zombies mode: game mode 5, zombie / run / event / parts / build / interact / workbench messages (ZUse..ZBench at
    //     the end of Msg), built weapons replicated to clients as build text, the window-opening collision layer (Clip)
    // 19: Zombies dropped builds: bleeding out drops your built weapons as pickups (ZPickup at the end of Msg: appear / gone)
    // 20: team selection: TeamPick / TeamReply at the end of Msg, the team rules and every player's pick at the end of the mode state
    // 21: score limits scale with the player count (ScoreLimits.cs): the mode state's last U16 is the current mode's score
    //     limit (free-for-all kills, team kills, hill points or captures), no longer only the TDM kill limit
    // 22: game modes are modules (Source/Game/Modes): the mode state no longer carries the hill and the flags for every
    //     mode; after the team picks comes the active mode's own tail (a version byte, a U16 length, then whatever the
    //     mode's GameModeModule.WriteState writes: KotH 7 bytes, CTF 36, others none). Game mode 6 is the Dev-only template.
    // 23: buildings (Buildings folder, the Building op with a "name"): BuildingUse / BuildingEvent / BuildingState at the end
    //     of Msg (doors open, shut, take damage and break, windows break; the join-in-progress state), the Doors physics layer
    // 24: prestige hats (GameCosmetics.cs): the Hello carries prestige and level after the name; every roster entry ends
    //     with prestige, level and hat (3 bytes)
    // 25: cosmetic loadout (GameCosmeticLoadout.cs): Msg.Loadout ends with the hat byte (after the kill streaks); the
    //     server keeps it only if the player's reported prestige unlocks it, and the roster carries it (protocol 24).
    // 26: frenzy per weapon class (Source/Game/Frenzy): every snapshot slow entry ends with the player's frenzy level
    //     and frenzy class (+ the host's frenzy-buffs bit), 2 bytes (Snapshot.SlowBytes 16 -> 18).
    // 27: tactical equipment (GameTactical.cs): ThrowableKind gains Flash .. Stim (Msg.Throw / Loadout bytes), and the
    //     new Msg.TacState (devices with the snapshots) and Msg.TacEvent (flash / stun, intercepts, pings...) at the end.
    // 28: ragdolls and blast knockback (Knockback.cs): Msg.Knock at the end of Msg (server -> the pushed client: input
    //     sequence + impulse, put into the prediction replay); blasts change living players' velocity on the server.
    // 29: eight more kill streaks (KillStreaksMore.cs): Msg.StreakState carries 16 earned-streak bits and armour (8 -> 12
    //     bytes), Msg.StreakEvent gains kinds 2-4 (effect ended, crate taken, streak blast), StreakIds 8-15 on the wire.
    // 30: launchers (Source/Game/Launchers): WeaponClass.Launcher weapons, the new Msg.RocketSpawn / RocketSync /
    //     RocketBoom / RocketDetonate at the end, and the Fire packet's trailing lock-on byte.
    // 31: landmines and prop respawns (GameMines.cs): Msg.PropBreak's kind byte gains Game.PropRestoreFlag (0x40, a prop
    //     placed with "respawn" is back), and kill id 254 (Game.LandmineKillId, "Landmine") for neutral blasts.
    // 32: the pre-match lobby room (PreMatchLobby.cs): MatchPhase.Lobby (4) in snapshots, the server info and MatchStart,
    //     and Msg.LobbyState / ChatSay / ChatLine at the end of Msg (lobby settings and pings, lobby chat).
    // 33: blood marks (BodyBlood.cs): every snapshot fast entry ends with the player's 32-bit blood word (hit body parts,
    //     generation, last hit direction; Snapshot.FastBytes 24 -> 28).
    // 34: meaty gibs (Gibs.cs, GameGibs.cs): bit 1 of Msg.Kill's headshot byte is "gibbed" (blown apart by a blast).
    // 35: destructible buildings (GameDestruction.cs): Msg.StructBreak / StructState at the end of Msg; destructible maps'
    //     fingerprints include their chunks (MapDestruction.Map.cs).
    // 36: in-match text chat and voice chat (LobbyChat.cs, GameVoice.cs): ChatSay gains a flags byte (team), ChatLine gains
    //     flags (team / dead / system) and the sender's team; Msg.VoiceUp / VoiceDown / VoiceCtl / VoiceInfo at the end of Msg.
    // 37: the Co-op mode (Source/Game/Pve, docs/pve/DESIGN.md): game mode 7, Msg.PEnts / PEvent at the end of Msg (enemy
    //     soldiers and co-op events), the Co-op module's mode-state tail (the objective panel), kill id 253 (killed by an enemy).
    // 38: the weapon workbench's barrels and Chain fire (docs/zombies/WEAPON-BUILDER.md): barrels fit every receiver (the
    //     receiver sizes them), and a build text's style field is honoured again (volley / rotate = the Chain fire box).
    //     The text format is unchanged, but servers and clients must agree on what a build fires like and what fits.
    // 39: the Test Range mode (Source/Game/Modes/TestRange): game mode 8, free-for-all that never ends (an older build would
    //     show it as free-for-all with a score limit and a clock that never apply).
    // 40: Zombies milestone 3 (ZombiesPowerUps.cs, ZombiesAmmoEffects.cs): Msg.ZWorld ends with the floating power-ups
    //     and the Insta-Kill / Double Points timers; ZEventKind MaxAmmo and Fx (a position after the text); bits 6-7 of a
    //     zombie's state byte in Msg.ZEnts (burning, zapped).
    // 41: Zombies disassembly (ZombiesDisassemble.cs): Msg.ZBench kind 3 takes a slot's build apart; Msg.ZBuild flag 4 says
    //     the slot is empty (no text; an empty gun slot 1-2 holds "Empty hands"), also sent to joiners for empty slots.
    // 43: zombie animations (ZombieAnimNet.cs): in Msg.ZEnts a tearing or climbing zombie's yaw byte carries its animation
    //     byte (the board cycle / the climb's progress; it faces its window anyway), still 12 bytes a zombie.
    //     (42 is another feature's number.)
    // 44: the weapon workbench's Action parts (docs/zombies/WEAPON-BUILDER.md, "Actions: the fire type is a part"): a new
    //     part category (network part ids of the older categories are unchanged; actions sort last), every receiver has
    //     an "action" socket and its fire type and rate come from the action in it. Build texts carry the actions; the
    //     server takes them as sent (no stock actions added), so both ends must agree on the part set and the rules.
    // 45: the APPEARANCE screen's look (Source/Cosmetics/Appearance.cs): 3 bytes after the profile in Msg.Loadout, and
    //     3 more per player after the hat in Msg.Roster (a nibble per axis, 0 = Auto; bots always Auto).
    // 46: attachment kits are 5 bits a slot (Source/Weapons/Attachments.cs: up to 31 attachments per slot; the optic slot
    //     was full at 15): kits are U32 instead of U24 in Msg.Loadout, Msg.Roster, the death cam and the rocket messages.
    // 47: the Zombies round flow (Source/Game/ZombiesFlow.cs): Msg.ZWorld's player flags gain bit 3 (ready for the next
    //     round) and bit 4 (counts for ready-up), and ZWorld ends with a byte (bit 0: the break waits for ready-up); Msg.Ready
    //     during a Zombies match means ready for the next round. Downed players crawl (same input bits, capped speed).
    // 48: the host's gravity setting (Source/Game/GameGravity.cs, 25-200%): a byte at the end of the Welcome and after the
    //     auto-start in Msg.LobbyState, so clients predict falls, jumps and grenades the host's way.
    // 49: the Zombies Infinite Ammo power-up (Source/Game/ZombiesPowerUps.cs): ZPowerKind 5 in Msg.ZWorld's floating
    //     power-ups, and a third timer (Infinite Ammo's tenths of a second left) after Insta-Kill's and Double Points'.
    // 50: zombie voices (Source/Audio/ZombieSounds.cs): bit 3 of a zombie's state byte in Msg.ZEnts is the head bit (a head
    //     hit in the last 4 ticks while alive, a headshot kill once dead); the state is the low 3 bits.
    // 51: Zombies slot machines (Source/Game/ZombiesSlots.cs): Msg.ZWorld ends with the machines (a count, then 11 bytes
    //     each: state, heat, owner, spin counter, three reel symbols, result / tier / auto-scrapped, the part, tenths left).
    // 52: lobby settings in the lobby room (Source/UI/UiLobbyRoom.cs): Msg.LobbyState's flags gain Destruction (32), Allow
    //     voice chat (64) and dead players' chat private (128), and a bot gear byte follows the gravity byte.
    // 53: Zombies answers (2026-10-26): a slot machine's result can be 4 (a melee build: its part field is then an index into
    //     the standard builds) and the reels gain the blade symbol (9).
    // 54: Zombies box disassembly fix (2026-10-26): Msg.ZBench kind 5 takes the mystery box's OFFERED build apart (the slot byte
    //     is ignored); kind 3 (a slot's build) now only works at a workbench.
    // 55: per-category Zombies slot machines (Source/Game/ZombiesSlotWin.cs): Msg.ZSlotWin (machine, player, result, part or
    //     melee build index, tier, flags) shows every win to everyone; a map's slot machine may name a part "category".
    // 56: Zombies without scrap (2026-10-27): Msg.ZRun loses its scrap count (just the loose parts); a slot machine result
    //     has no scrap value (0 nothing, 1 points, 2 part, 3 melee) and its reels no cog (0 skull, 1 coin, 2-7 gems, 8 blade);
    //     the result byte's bit 7 (and Msg.ZSlotWin's flag bit 0) now means a part you held plenty of paid points instead;
    //     Msg.ZBench kinds 1 and 2 (break down, craft) are gone.
    // 57: Zombies has 12 perks (seven new ones, 2026-10-27): the perk flags in Msg.ZWorld are 16 bits, and bit 5 of a
    //     player's flags says the armour plate (Bulwark Brew) is up.
    // 58: Zombie hordes (hundreds alive late game): Msg.ZEnts is sent to each client on its own, interest-managed (the
    //     zombies that matter most to that client, at most 80 a packet), with a 16-bit horde size and entry count; an
    //     entry carries the window byte only outside, at a window or swiping (11 or 12 bytes).
    // 59: Zombies start round and test kit (Source/Game/ZombiesStartRound.cs): Msg.LobbyState ends with the start round
    //     and a Zombies flags byte (bit 0: the test kit).
    // 60: Weapon tweak window (Source/UI/UiWeaponTweaks.cs): Msg.WeaponTweaks (class multipliers: count, then class, stat,
    //     value; per-weapon overrides: u16 count, then weapon id, field index, value), sent on a change and to joiners.
    // 61: map logic (Source/Game/GameLogic.cs, Maps/README.md "Map logic"): Msg.Logic at the end of Msg (a LogicMsgKind byte
    //     first: entity flags, a button or lever used, a mover's control clock as three doubles, a sound, a notice, the
    //     joiner's full flags); movers can be started and stopped, so their pose follows that clock.
    // 62: Grenades in Zombies (2026-10-27): every player entry in Msg.ZWorld ends with the grenade kind and how many
    //     are left (the server tops them up every round; ZombiesRules.json "grenades").
    // 63: Shots leave the muzzle that fired (Source/Game/GameMuzzle.cs): Msg.Shot from a built gun with several barrels
    //     ends with a count and each end's barrel index (one byte each), after the ends and the wall origin.
    // 64: independent receiver fire rates (Source/Game/GameFireGroups.cs): Msg.Fire ends with the lock-target byte (0xFF
    //     when none) and a u16 mask of the receivers that fired on a volley build (0 = all); the server budgets each one.
    // 65: Zombie dismemberment (Source/Game/ZombieLimbs.cs): zombie ids are 15 bits, and in Msg.ZEnts the top bit of an
    //     entry's id says a limbs byte follows the entry (bits 0-3: right arm, left arm, right leg, left leg lost; both legs is
    //     a crawler), 11-13 bytes a zombie.
    // 66: Zombies casino tables (Source/Game/ZombiesCasino.cs): Msg.ZCasino (a table's state, server -> all) and
    //     Msg.ZCasinoAct (a player's move, client -> server) at the end of Msg, after Msg.Logic.
    // 67: Modular grenades (Source/Game/GameGrenades.cs): builds in the Zombies grenade slot (Msg.ZBuild slot 5);
    //     ThrowableKind.Built; Msg.Throw of a built grenade ends with the cook time (1/50 s), Msg.ProjSpawn with what it is
    //     (thrown, bomblet, aftershock) and Msg.Explode with the payload, radius and duration (tenths) and that byte;
    //     Msg.ZGrenade at the end of Msg (remote detonation, a lure's closing bang).
    // 68: The Cathedral's bell siege (Source/Game/ZombiesClimb.cs): zombie state 7 (ZState.Scaling: on a climbable face)
    //     in Msg.ZEnts' state bits; map logic "zclimb" and Zombies areas with heights (map data, not the wire).
    // 69: Zombies slot consolations and unlimited loose parts (2026-10-27): Msg.ZRun is a u16 count of kinds, then each
    //     name and a u32 count (was a byte count of at most 200 kinds, 255 each); a slot machine's result byte in Msg.ZWorld
    //     uses bit 6 and Msg.ZSlotWin's flags bit 1 for a consolation part (a blank pays one: result 2, the reels' skull).
    // 70: Zombies drop items (Source/Game/ZombiesDropItems.cs): Msg.ZDropItem (client -> server: drop the gun in hand or
    //     loose parts) and Msg.ZPartDrop (server -> all: loose-part pickups) at the end of Msg; Msg.ZPickup's appear
    //     message ends with the round the dropped build lasts to (u16, 0 = time only).
    // 71: The mass horde's crowd (docs/zombies/MASS-HORDE.md, Source/Game/ZombiesCrowdNet.cs): Msg.ZCrowd at the end of Msg
    //     (per-cell crowd counts and headings, changes or a keyframe), sent only on maps with a "crowd".
    // 72: The Zombies trader (Source/Game/ZombiesTrader.cs): Msg.ZTrader (the trader and his shelf, server -> all) and
    //     Msg.ZTraderBuy (a purchase: item, visit, count; no purchase limit, client -> server) at the end of Msg.
    // 73: Zombie types (Source/Game/ZombieTypes.cs): in Msg.ZEnts the limbs byte's top four bits are the zombie's type and a
    //     typed zombie carries a type state byte (plates, grabbing); Msg.ZWorld's player flags bit 6 is "held by a grabber".
    // 74: Back to lobby (Source/Game/GameHostBye.cs): Msg.HostBye at the end of Msg (server -> clients: why the host is
    //     closing the connection), and the protocol number at the end of Msg.Welcome and Msg.Hello: a different build is
    //     refused when it joins, with the reason, instead of misreading packets later.
    // 75: Active ragdolls (Source/Render/Ragdolls.cs): Msg.Kill ends with the death (the hit body part and its cause in one
    //     byte, the overkill in HP and as a share of the hit); a dead zombie's health byte in Msg.ZEnts is its death info
    //     (Game.ZDeathInfo: overkill share, head / melee / explosive).
    // 76: Steam (Phases 1 and 2, merged together; docs/steam/STEAM.md). Msg.SteamIds at the end of Msg (a client's own
    //     Steam ID after its Hello; the server's table of every Steam player's id with each roster, sent only when at least
    //     one player is on Steam) and Msg.SteamAuth after it (a client's Steam session ticket for the host; the host's
    //     refusal with a reason). Packets travel the same over RakNet and Steam sockets. A game without Steam sends nothing new.
    // 77: Sandbox mode and constructs (docs/sandbox/CONSTRUCTS.md): GameMode.Sandbox at the end of the modes; Msg.CBody,
    //     Msg.CPose and Msg.CAct at the end of Msg (construct structure and health, poses at 20 Hz, players' tool and edit
    //     actions); the Sandbox mode-state tail carries each player's tool, held part, seat and edit in progress.
    // 78: Sandbox constructs milestone 2 (docs/sandbox/CONSTRUCTS.md): construct structure carries each part's logic setting
    //     and signal, joint types, lock and wired flags, and the wires; poses carry joint speeds and the signal parts' values;
    //     Msg.CAct gains Wire, Unwire, Setting and SeatKeys; the Sandbox state tail's tool can be the linking tool (version 2).
    // 79: the Sandbox debug parts menu (docs/sandbox/CONSTRUCTS.md): CActKind.Rack carries a pick byte (0: the rack's next
    //     kind; n: part id n - 1, from the menu) and CActKind.Blueprint (a blueprint index) builds one in front of the player.
    // 80: seated Sandbox players (docs/sandbox/CONSTRUCTS.md "Seats"): a seated player's snapshot entry carries the seat
    //     (construct id and seat part, in the mantle extension with mantle ticks 0) and a yaw relative to the seat's facing.
    // 81: Sandbox salvage (docs/sandbox/CONSTRUCTS.md "Salvage and the inventory"): a construct's Msg.CBody carries its
    //     owner and a "damaged" flag; CBodyKind.Inventory sends a player their own inventory (part counts, scrap, materials).
    // 82: Sandbox construct weapons (docs/sandbox/CONSTRUCTS.md "Construct weapons"): Msg.CFire at the end of Msg (each tick's
    //     shots, blasts and cook-offs), CBodyKind.Ammo (rounds and fire changes), and a construct's structure ends with the
    //     parts holding rounds or on fire.
    // 83: Sandbox milestone 3 links and power (docs/sandbox/CONSTRUCTS.md "Milestone 3"): CBodyKind.Links (couplings and wires
    //     between constructs), the charge, power flags and gear in CBody and CPose (and the fuel in CPose), CAct.Wire's to-construct.
    // 84: Sandbox free placement (docs/sandbox/CONSTRUCTS.md "New constructs"): CAct.PlaceNew carries the surface normal, the
    //     part's Rot24 turn and grid/anchor flags; CAct.Join (weld two constructs into one); a construct's "pinned" flag in CBody.
    // 85: Sandbox physical storage (docs/sandbox/CONSTRUCTS.md "Physical storage"): a construct's structure lists its storage
    //     parts' store handles, CBodyKind.Store sends one container's contents, CActKind StoreIn / TakeStored / Bin;
    //     every player's backpack: CBodyKind.Pack (a player's pack contents), CActKind Stow / Draw.
    // 86: Sandbox drivetrain parts (docs/sandbox/CONSTRUCTS.md "Drivetrain parts"): the engine's rpm and the drive note in
    //     CBody and CPose.
    // 87: Sandbox glass (docs/sandbox/CONSTRUCTS.md "Glass"): CFireKind.Shatter in Msg.CFire (a glass part broke: where, its
    //     half size), so every PC bursts it into shards.
    // 88: Sandbox physics gun (docs/sandbox/CONSTRUCTS.md "The physics gun"): CActKind Grab / GrabSteer / GrabRelease /
    //     GrabFreeze / UnfreezeAll; CBody's frozen flag and who froze it; the mode-state tail (version 3) carries each
    //     player's held construct and grab point (the beam); the physics gun's tool byte (8).
    // 89: Sandbox configure tool (docs/sandbox/CONSTRUCTS.md "The configure tool"): CBody ends with the part settings off
    //     their defaults; CActKind.Configure (the settings of one or more parts of a kind); the configure tool's tool byte (4).
    // 90: Sandbox routed links (docs/sandbox/CONSTRUCTS.md "Routed links"): Msg.CBody carries a construct's routed links
    //     (data, power, pipe, shaft, chute: endpoints, junction, nodes on the 1 cm grid, segment health) in place of its wires;
    //     CAct.Route / RouteEdit at the end of CActKind; part ids are two bytes everywhere (CBody parts, stores, the inventory,
    //     the mode-state tail's held part, CAct.Rack's pick).
    // 91: Sandbox spawns (docs/sandbox/CONSTRUCTS.md "Spawning bots and zombies"): CAct.Spawn / ClearSpawns at the end of
    //     CActKind; routed data links may name a part's data port (0x80 | k: the Bot and Zombie Spawners' inputs and
    //     outputs); a part takes up to 12 settings (CAct.Configure); the Sandbox relays the zombie messages (ZEnts, ZEvent, ZCrowd).
    public const byte ProtocolVersion = 91;
    private static readonly byte[] Magic = "COS1"u8.ToArray();

    public byte[] Encode()
    {
        var name = Encoding.ASCII.GetBytes(Name.Length > 32 ? Name[..32] : Name);
        var map = Encoding.UTF8.GetBytes(MapName.Length > 40 ? MapName[..40] : MapName);
        var b = new List<byte>(Magic);
        b.AddRange([Version, (byte)Mode, Players, MaxPlayers, Bots, (byte)Phase, (byte)Port, (byte)(Port >> 8), (byte)name.Length]);
        b.AddRange(name);
        b.Add((byte)map.Length);
        b.AddRange(map);
        b.Add((byte)Physics);
        var rotation = Encoding.ASCII.GetBytes(Rotation.Length > 24 ? Rotation[..24] : Rotation);
        var build = Encoding.ASCII.GetBytes(Build.Length > 23 ? Build[..23] : Build);
        var extensions = InstanceId != 0 || ListPort != 0 || build.Length > 0 || Gravity != 100;
        if (rotation.Length > 0 || extensions)
        {
            b.Add((byte)rotation.Length);
            b.AddRange(rotation);
        }
        // Optional tagged extensions after the rotation (discovery only, so no protocol bump: older builds stop
        // reading after the rotation). 'I': this hosting's random id, so the browser shows a game found on the LAN and
        // through a server list once. 'L': the server list this host is on (port, then the host name; an empty name
        // means "on this host's own address"), so players who find the game learn the list (ListLearner).
        if (InstanceId != 0)
            b.AddRange([(byte)'I', (byte)InstanceId, (byte)(InstanceId >> 8), (byte)(InstanceId >> 16), (byte)(InstanceId >> 24)]);
        if (ListPort != 0)
        {
            var host = Encoding.ASCII.GetBytes(ListHost.Length > 64 ? ListHost[..64] : ListHost);
            b.AddRange([(byte)'L', (byte)ListPort, (byte)(ListPort >> 8), (byte)host.Length]);
            b.AddRange(host);
        }
        // 'B': the host's release version (BuildInfo.cs), for "your build is older" in the browser.
        if (build.Length > 0)
        {
            b.Add((byte)'B');
            b.Add((byte)build.Length);
            b.AddRange(build);
        }
        // 'G' (last: older builds stop at an unknown tag): the host's gravity setting in percent (GameGravity.cs), when not 100.
        if (Gravity != 100)
            b.AddRange([(byte)'G', Gravity]);
        return b.ToArray();
    }

    /// <summary>Finds and decodes server info anywhere in a packet (RakNet puts a header and timestamp before it).</summary>
    public static ServerInfo? Find(ReadOnlySpan<byte> packet)
    {
        var at = packet.IndexOf(Magic);
        if (at < 0)
            return null;
        var d = packet[(at + Magic.Length)..];
        if (d.Length < 9 || d.Length < 9 + d[8])
            return null;
        var name = Encoding.ASCII.GetString(d.Slice(9, d[8]));
        var rest = d[(9 + d[8])..];
        var map = rest.Length >= 1 && rest.Length >= 1 + rest[0] ? Encoding.UTF8.GetString(rest.Slice(1, rest[0])) : "?";
        var physics = rest.Length >= 1 && rest.Length >= 2 + rest[0] && rest[1 + rest[0]] == (byte)PhysicsMode.Swept ? PhysicsMode.Swept : PhysicsMode.Classic;
        var rotation = "";
        var build = "";
        uint instance = 0;
        var listHost = "";
        ushort listPort = 0;
        byte gravity = 100;
        if (rest.Length >= 1 && rest.Length >= 3 + rest[0])
        {
            var tail = rest[(2 + rest[0])..]; // after the map name and the physics byte
            if (tail.Length >= 1 + tail[0])
            {
                rotation = Encoding.ASCII.GetString(tail.Slice(1, tail[0]));
                var ext = tail[(1 + tail[0])..];
                while (ext.Length > 0)
                {
                    if (ext[0] == (byte)'I' && ext.Length >= 5)
                    {
                        instance = (uint)(ext[1] | (ext[2] << 8) | (ext[3] << 16) | (ext[4] << 24));
                        ext = ext[5..];
                    }
                    else if (ext[0] == (byte)'L' && ext.Length >= 4 && ext.Length >= 4 + ext[3])
                    {
                        listPort = (ushort)(ext[1] | (ext[2] << 8));
                        listHost = Encoding.ASCII.GetString(ext.Slice(4, ext[3]));
                        ext = ext[(4 + ext[3])..];
                    }
                    else if (ext[0] == (byte)'B' && ext.Length >= 2 && ext.Length >= 2 + ext[1])
                    {
                        build = BuildInfo.ParseRelease(Encoding.ASCII.GetString(ext.Slice(2, ext[1])))?.ToString() ?? "";
                        ext = ext[(2 + ext[1])..];
                    }
                    else if (ext[0] == (byte)'G' && ext.Length >= 2)
                    {
                        gravity = ext[1] is >= Game.MinGravityPercent and <= Game.MaxGravityPercent ? ext[1] : (byte)100;
                        ext = ext[2..];
                    }
                    else
                        break; // an unknown (newer) or cut-off extension: stop here
                }
            }
        }
        // Anyone can answer a ping: names are capped printable ASCII before the browser shows them.
        return new ServerInfo(NetSafety.CleanText(name, 32), NetSafety.CleanText(map, 40), d[2], d[3], d[4], (MatchPhase)d[5], (ushort)(d[6] | (d[7] << 8)), d[0],
            (GameMode)d[1], physics, NetSafety.CleanText(rotation, 24), instance, Settings.CleanAddress(NetSafety.CleanText(listHost, 64)), listPort, build, gravity);
    }
}

/// <summary>How a listed game's build compares with ours (the server browser shows it and only lets compatible ones join).</summary>
public enum BuildCompat
{
    Same, // same protocol and the same (or an unknown) release
    OtherBuild, // same protocol, a different release: joinable
    ServerNewer, // newer protocol: update to join
    ServerOlder, // older protocol: the host must update
}

public static class BuildCompatText
{
    /// <summary>Compares a game's protocol and release (<see cref="ServerInfo.Build"/>, "" when unknown) with ours.</summary>
    public static BuildCompat Compare(byte protocol, string build, byte ourProtocol, Version? ourRelease)
    {
        var theirs = BuildInfo.ParseRelease(build);
        if (protocol != ourProtocol)
        {
            if (theirs != null && ourRelease != null && theirs != ourRelease)
                return theirs > ourRelease ? BuildCompat.ServerNewer : BuildCompat.ServerOlder;
            return protocol > ourProtocol ? BuildCompat.ServerNewer : BuildCompat.ServerOlder;
        }
        return theirs != null && ourRelease != null && theirs != ourRelease ? BuildCompat.OtherBuild : BuildCompat.Same;
    }

    /// <summary>The line under a game in the browser ("" for a game on our build).</summary>
    public static string Describe(BuildCompat c, string build) => c switch
    {
        BuildCompat.ServerNewer => $"Your build is older - update to join{(build.Length > 0 ? $" (host has v{build})" : "")}",
        BuildCompat.ServerOlder => $"The host's build is older{(build.Length > 0 ? $" (v{build})" : "")} - they must update before you can join",
        BuildCompat.OtherBuild => $"Host has v{build}, you have v{BuildInfo.Version} (compatible)",
        _ => "",
    };
}

/// <summary>Where the server browser heard about a game (a game can be found several ways; the row shows them all).</summary>
[Flags]
public enum ServerSource : byte
{
    None = 0,
    Lan = 1, // answered the LAN broadcast
    List = 2, // listed by a server list (master server)
    Steam = 4, // a Steam lobby (SteamLobbies.cs): joined over Steam sockets
}

/// <summary>
/// Finds games to join:
///   - on your network, by broadcasting an unconnected ping to the usual game ports;
///   - on the internet, by asking every server list (master server) in Settings, any shipped in masters.txt and any
///     running in this game, for their advertised games, then pinging each of them directly for live details and latency.
/// The same game found several ways (LAN and a list, or two lists) is shown once (<see cref="Merge"/>). Games from a
/// different build are shown greyed with their protocol version instead of being hidden.
/// </summary>
public sealed class ServerBrowser : IDisposable
{
    public sealed class Entry
    {
        public required string Address;
        public required ServerInfo Info;
        public int PingMs;
        public double LastSeen;
        public ServerSource Sources;

        public bool FromMaster => (Sources & ServerSource.List) != 0;
        public bool FromLan => (Sources & ServerSource.Lan) != 0;
        public bool FromSteam => (Sources & ServerSource.Steam) != 0;
        /// <summary>Only reachable over Steam (a lobby): joined through it, no ping to show.</summary>
        public bool SteamOnly => Sources == ServerSource.Steam;

        /// <summary>Same build (protocol version) as this game: only these can be joined.</summary>
        public bool VersionOk => Info.Version == ServerInfo.ProtocolVersion;

        public string Key => $"{Address}:{Info.Port}";
    }

    private readonly NetPeer _net = NetPeer.Discovery();
    private readonly Dictionary<string, Entry> _servers = new();
    private readonly Dictionary<string, Entry> _steam = new(); // Steam lobbies, by "steam:lobby:ID" (SteamLobbies.cs)
    private readonly Dictionary<string, double> _pingSent = new(); // "ip:port" or ":port" (LAN broadcast) -> time
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _nextRefresh;
    private readonly HashSet<string> _fromMaster = new();
    private readonly HashSet<string> _listsReplied = new();
    private int _listsAsked, _listedGames;
    private List<Entry> _merged = new();
    private bool _dirty;

    /// <summary>Every game found, one row per game (see <see cref="Merge"/>): joinable ones first, then by ping.</summary>
    public IReadOnlyList<Entry> Servers
    {
        get
        {
            if (_dirty)
            {
                _merged = Merge(_servers.Values.Concat(_steam.Values));
                _dirty = false;
            }
            return _merged;
        }
    }

    public string MasterStatus { get; private set; } = "";
    public bool Searching => Now - _lastRefresh < 1.5;
    private double _lastRefresh = -10;

    private double Now => _clock.Elapsed.TotalSeconds;

    public ServerBrowser()
    {
        CallOfSlop.Platform.Steam.SteamPlatform.Lobby.ListChanged += OnSteamLobbies;
    }

    /// <summary>Steam's public lobbies of this game arrived: one row each, joined through the lobby (no address, no ping).</summary>
    private void OnSteamLobbies()
    {
        _steam.Clear();
        foreach (var l in CallOfSlop.Platform.Steam.SteamPlatform.Lobby.Listed)
            _steam[l.Address] = new Entry { Address = l.Address, Info = l.Info, PingMs = 0, LastSeen = Now, Sources = ServerSource.Steam };
        _dirty = true;
    }

    /// <summary>
    /// One row per game: entries with the same hosting id (<see cref="ServerInfo.InstanceId"/>) are one game reached by
    /// different addresses (LAN and internet, loopback); the row keeps the lowest-ping address and every source.
    /// </summary>
    public static List<Entry> Merge(IEnumerable<Entry> entries)
    {
        var rows = new List<Entry>();
        foreach (var g in entries.GroupBy(e => e.Info.InstanceId != 0 ? $"#{e.Info.InstanceId:x8}" : e.Key))
        {
            var best = g.OrderBy(e => e.SteamOnly ? 1 : 0).ThenBy(e => e.PingMs).First(); // a direct address before the Steam lobby of the same game
            var sources = g.Aggregate(ServerSource.None, (a, e) => a | e.Sources);
            rows.Add(sources == best.Sources ? best : new Entry { Address = best.Address, Info = best.Info, PingMs = best.PingMs, LastSeen = best.LastSeen, Sources = sources });
        }
        return rows.OrderByDescending(e => e.VersionOk).ThenBy(e => e.PingMs).ToList();
    }

    /// <summary>Ask again now (also happens every few seconds on its own).</summary>
    public void Refresh(Settings s)
    {
        _lastRefresh = Now;
        _nextRefresh = Now + 6;
        CallOfSlop.Platform.Steam.SteamPlatform.Lobby.RequestList(); // Steam lobbies (nothing without Steam)
        // LAN: the game port people use plus a few next to it
        foreach (var port in new[] { s.Port, Program.DefaultPort, (ushort)(Program.DefaultPort + 1), (ushort)(Program.DefaultPort + 2) }.Distinct())
        {
            _pingSent[$":{port}"] = Now;
            _net.PingHost("255.255.255.255", port);
        }
        // Internet: every server list we know (names are looked up in the background, never on this thread)
        var lists = s.AllMasters;
        _listsAsked = lists.Count;
        _listsReplied.Clear();
        _listedGames = 0;
        foreach (var (host, port) in lists)
            if (HostNames.TryResolve(host) is { } ip)
                _net.Advertise(ip, port, MasterServerApp.ListQuery);
        MasterStatus = lists.Count == 0 ? "" : $"Asking {lists.Count} server list(s) for games...";
        // Re-ping everything we already know so pings and player counts stay fresh
        foreach (var e in _servers.Values.ToList())
            PingServer(e.Address, e.Info.Port);
        // Forget games that stopped answering
        foreach (var gone in _servers.Where(kv => Now - kv.Value.LastSeen > 15).Select(kv => kv.Key).ToList())
        {
            _servers.Remove(gone);
            _dirty = true;
        }
    }

    private void PingServer(string ip, ushort port)
    {
        if (_pingSent.Count > 512)
            _pingSent.Clear(); // a hostile list can't make this grow without bound
        _pingSent[$"{ip}:{port}"] = Now;
        _net.PingHost(ip, port);
    }

    public void Tick(Settings s)
    {
        if (Now >= _nextRefresh)
            Refresh(s);
        _net.PollRaw((from, data) =>
        {
            try
            {
                OnBrowserPacket(from, data);
            }
            catch (Exception ex)
            {
                NetSafety.Drop("a server-browser reply", data, ex); // a bad reply is dropped, the browser keeps going
            }
        });
    }

    private void OnBrowserPacket(SystemAddress from, ReadOnlySpan<byte> data)
    {
        var ip = from.ToString(false, '|');
        switch ((DefaultMessageIDTypes)data[0])
        {
            case DefaultMessageIDTypes.UnconnectedPong when ServerInfo.Find(data) is { } info:
            {
                var key = $"{ip}:{info.Port}";
                var lan = !_pingSent.ContainsKey(key) && _pingSent.ContainsKey($":{info.Port}");
                var sent = _pingSent.TryGetValue(key, out var t) ? t : _pingSent.GetValueOrDefault($":{info.Port}", _lastRefresh);
                var ping = (int)Math.Clamp((Now - sent) * 1000, 1, 9999);
                if (!_servers.TryGetValue(key, out var e))
                {
                    if (_servers.Count >= 256)
                        break;
                    _servers[key] = e = new Entry { Address = ip, Info = info };
                }
                e.Info = info;
                e.PingMs = ping;
                e.LastSeen = Now;
                e.Sources |= (lan ? ServerSource.Lan : ServerSource.None) | (_fromMaster.Contains(key) ? ServerSource.List : ServerSource.None);
                if (e.Sources == ServerSource.None)
                    e.Sources = NetAddr.IsLocal(ip) ? ServerSource.Lan : ServerSource.List;
                _dirty = true;
                break;
            }
            case DefaultMessageIDTypes.AdvertiseSystem when MasterServerApp.ParseList(data) is { } list:
            {
                if (_listsReplied.Add($"{ip}:{from.DebugPort}"))
                    _listedGames += list.Count;
                MasterStatus = _listedGames == 0
                    ? $"No games on your server list(s) right now ({_listsReplied.Count} of {_listsAsked} answered)."
                    : $"{_listedGames} game(s) on {_listsReplied.Count} of {_listsAsked} server list(s).";
                foreach (var (addr, port) in list.Take(64))
                {
                    if (_fromMaster.Count > 512)
                        _fromMaster.Clear();
                    _fromMaster.Add($"{addr}:{port}");
                    PingServer(addr, port);
                }
                break;
            }
        }
    }

    public void Dispose()
    {
        CallOfSlop.Platform.Steam.SteamPlatform.Lobby.ListChanged -= OnSteamLobbies;
        _net.Dispose();
    }
}

/// <summary>Address helpers for discovery: what kind of address an IPv4 string is.</summary>
public static class NetAddr
{
    public enum Kind
    {
        Public,
        Private, // 10/8, 172.16/12, 192.168/16, 169.254/16 and carrier-grade NAT 100.64/10
        Loopback,
        Invalid,
    }

    public static Kind KindOf(string? ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return Kind.Invalid;
        var b = a.GetAddressBytes();
        if (b[0] == 127)
            return Kind.Loopback;
        if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127))
            return Kind.Private;
        return Kind.Public;
    }

    /// <summary>Loopback or a private (LAN) address.</summary>
    public static bool IsLocal(string? ip) => KindOf(ip) is Kind.Loopback or Kind.Private;
}

/// <summary>
/// Host names (server lists typed as names, e.g. "mylist.example.com") looked up in the background, so the UI and the
/// game loop never wait on DNS. <see cref="TryResolve"/> returns the IPv4 address once known (literal addresses at once).
/// </summary>
public static class HostNames
{
    private static readonly Dictionary<string, (string? Ip, DateTime Until)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? TryResolve(string host)
    {
        if (System.Net.IPAddress.TryParse(host, out var lit))
            return lit.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? lit.ToString() : null;
        lock (Cache)
        {
            if (Cache.TryGetValue(host, out var c) && DateTime.UtcNow < c.Until)
                return c.Ip;
            if (Cache.Count > 64)
                Cache.Clear();
            Cache[host] = (c.Ip, DateTime.UtcNow.AddSeconds(30)); // one lookup at a time per name
        }
        _ = Task.Run(async () =>
        {
            string? ip = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                ip = (await System.Net.Dns.GetHostAddressesAsync(host, System.Net.Sockets.AddressFamily.InterNetwork, cts.Token)).FirstOrDefault()?.ToString();
            }
            catch (Exception)
            {
                // unknown name or no network: try again later
            }
            lock (Cache)
                Cache[host] = (ip, DateTime.UtcNow.AddSeconds(ip != null ? 300 : 30));
        });
        return null;
    }
}

/// <summary>
/// The master server (a "server list"): keeps the list of advertised games. Hosts send it a heartbeat every 20 s from
/// their game socket; browsers ask it for the list. It only stores addresses: everything else (name, map, players,
/// ping) comes straight from each game when the browser pings it.
/// It runs three ways: the dedicated "CallOfSlop.exe --master [port]" (tools\RunServerList.bat in the release), the
/// in-game "Run a server list for friends" option, or automatically inside a host's game when none of its lists answers
/// (<see cref="ListServer"/>, <see cref="HostListing"/>).
/// </summary>
public static class MasterServerApp
{
    public const ushort DefaultPort = 27050;
    public static readonly byte[] ListQuery = "COLQ"u8.ToArray();
    private static readonly byte[] HeartbeatTag = "COHB"u8.ToArray();
    private static readonly byte[] ListTag = "COLS"u8.ToArray();
    private static readonly byte[] IdTag = "LSID"u8.ToArray();

    /// <summary>The most games one list keeps (heartbeats from new addresses beyond this are ignored).</summary>
    public const int MaxGames = 512;

    /// <summary>List queries answered per second for one address (more are ignored: a reply is bigger than the query).</summary>
    public const int MaxQueriesPerSecond = 4;

    /// <summary>How a list server describes itself in its replies, and what it knows about its own addresses.</summary>
    public sealed class Options
    {
        /// <summary>0: dedicated or started by hand (preferred); 1: started automatically by a host (hands over to a 0, or to a lower id).</summary>
        public byte Rank;

        /// <summary>A random id, so a host can tell its own list from others and lists can agree which one stays.</summary>
        public uint Id = (uint)Random.Shared.Next(1, int.MaxValue);

        /// <summary>This PC's public IPv4 (STUN / UPnP), used for games that registered from this PC or its LAN.</summary>
        public volatile string? PublicIp;

        /// <summary>This PC's LAN IPv4, used for games that registered from this PC (loopback) when a LAN browser asks.</summary>
        public volatile string? LanIp;

        internal readonly Dictionary<string, (long Second, int Count)> Queries = new();
    }

    /// <summary>Heartbeat payload a host sends (its game port).</summary>
    public static byte[] Heartbeat(ushort gamePort) => [.. HeartbeatTag, (byte)gamePort, (byte)(gamePort >> 8)];

    public static List<(string Address, ushort Port)>? ParseList(ReadOnlySpan<byte> packet) => ParseListFull(packet, out _, out _);

    /// <summary>The list in a reply, plus the list server's rank and id (0, 0 from an older list server).</summary>
    public static List<(string Address, ushort Port)>? ParseListFull(ReadOnlySpan<byte> packet, out byte rank, out uint id)
    {
        rank = 0;
        id = 0;
        var at = packet.IndexOf(ListTag);
        if (at < 0)
            return null;
        var d = packet[(at + ListTag.Length)..];
        if (d.Length < 1)
            return null;
        var list = new List<(string, ushort)>();
        var count = d[0];
        var p = 1;
        var complete = true;
        for (var i = 0; i < count; i++)
        {
            if (p >= d.Length)
            {
                complete = false;
                break;
            }
            int len = d[p++];
            if (p + len + 2 > d.Length)
            {
                complete = false;
                break;
            }
            var addr = NetSafety.CleanText(Encoding.ASCII.GetString(d.Slice(p, len)), 64);
            p += len;
            var port = (ushort)(d[p] | (d[p + 1] << 8));
            p += 2;
            list.Add((addr, port));
        }
        if (complete && d.Length >= p + IdTag.Length + 5 && d.Slice(p, IdTag.Length).SequenceEqual(IdTag))
        {
            rank = d[p + 4];
            id = (uint)(d[p + 5] | (d[p + 6] << 8) | (d[p + 7] << 16) | (d[p + 8] << 24));
        }
        return list;
    }

    /// <summary>
    /// The address to hand a browser at <paramref name="querierIp"/> for a game that registered from <paramref name="entryIp"/>:
    /// a game on the list's own PC (loopback) or its LAN can't be reached at that address from outside, so outsiders get
    /// the list PC's public address (and LAN browsers its LAN address for a loopback game). Unknown addresses: unchanged.
    /// </summary>
    public static string AddressFor(string entryIp, string querierIp, string? publicIp, string? lanIp)
    {
        var entry = NetAddr.KindOf(entryIp);
        var q = NetAddr.KindOf(querierIp);
        if (entry == NetAddr.Kind.Loopback)
            return q == NetAddr.Kind.Loopback ? entryIp : q == NetAddr.Kind.Private ? lanIp ?? entryIp : publicIp ?? entryIp;
        if (entry == NetAddr.Kind.Private && q == NetAddr.Kind.Public)
            return publicIp ?? entryIp;
        return entryIp;
    }

    /// <summary>Handles everything waiting on the master's socket: heartbeats from hosts and list queries from browsers.</summary>
    public static void Pump(NetPeer net, Dictionary<string, (string Ip, ushort Port, DateTime Seen)> games, Options? options = null)
    {
        // A list runs this every 15 ms (in a host's own list, on its thread): one handler per thread, made once, instead
        // of a closure and a delegate every call.
        var p = _pumper ??= new Pumper();
        (p.Net, p.Games, p.Options) = (net, games, options ?? (p.Default ??= new Options()));
        net.PollRaw(p.Handler);
    }

    [ThreadStatic] private static Pumper? _pumper;

    private sealed class Pumper
    {
        public NetPeer? Net;
        public Dictionary<string, (string Ip, ushort Port, DateTime Seen)>? Games;
        public Options? Options, Default;
        public readonly NetPeer.RawPacketHandler Handler;

        public Pumper() => Handler = OnPacket;

        private void OnPacket(SystemAddress from, ReadOnlySpan<byte> data)
        {
            try
            {
                OnMasterPacket(Net!, Games!, Options!, from, data);
            }
            catch (Exception ex)
            {
                NetSafety.Drop("a master-server message", data, ex);
            }
        }
    }

    private static void OnMasterPacket(NetPeer net, Dictionary<string, (string Ip, ushort Port, DateTime Seen)> games, Options options, SystemAddress from, ReadOnlySpan<byte> data)
    {
        if ((DefaultMessageIDTypes)data[0] != DefaultMessageIDTypes.AdvertiseSystem)
            return;
        var ip = from.ToString(false, '|');
        var hb = data.IndexOf(HeartbeatTag);
        if (hb >= 0 && data.Length >= hb + 6)
        {
            var gamePort = (ushort)(data[hb + 4] | (data[hb + 5] << 8));
            if (gamePort == 0)
                return;
            var key = $"{ip}:{gamePort}";
            if (!games.ContainsKey(key))
            {
                if (games.Count >= MaxGames)
                    return;
                Console.WriteLine($"[master] + {key}");
            }
            games[key] = (ip, gamePort, DateTime.UtcNow);
            return;
        }
        if (data.IndexOf(ListQuery) >= 0)
        {
            var second = Environment.TickCount64 / 1000;
            var q = options.Queries.GetValueOrDefault(ip);
            q = q.Second == second ? (second, q.Count + 1) : (second, 1);
            if (options.Queries.Count > 1024)
                options.Queries.Clear();
            options.Queries[ip] = q;
            if (q.Count > MaxQueriesPerSecond)
                return;
            var reply = new List<byte>(ListTag);
            var live = games.Values.Where(g => DateTime.UtcNow - g.Seen < TimeSpan.FromSeconds(60)).Take(60).ToList();
            reply.Add((byte)live.Count);
            foreach (var g in live)
            {
                var a = Encoding.ASCII.GetBytes(AddressFor(g.Ip, ip, options.PublicIp, options.LanIp));
                reply.Add((byte)a.Length);
                reply.AddRange(a);
                reply.Add((byte)g.Port);
                reply.Add((byte)(g.Port >> 8));
            }
            reply.AddRange(IdTag);
            reply.Add(options.Rank);
            reply.AddRange([(byte)options.Id, (byte)(options.Id >> 8), (byte)(options.Id >> 16), (byte)(options.Id >> 24)]);
            net.Advertise(ip, from.DebugPort, reply.ToArray());
        }
    }

    /// <summary>Drops games whose last heartbeat is over a minute old; returns their keys.</summary>
    public static List<string> Expire(Dictionary<string, (string Ip, ushort Port, DateTime Seen)> games)
    {
        // (Runs every 15 ms in a list: a loop, and no list at all while nothing expires.)
        List<string>? dead = null;
        var now = DateTime.UtcNow;
        foreach (var kv in games)
            if (now - kv.Value.Seen > TimeSpan.FromSeconds(60))
                (dead ??= new List<string>()).Add(kv.Key);
        if (dead == null)
            return NoneExpired;
        foreach (var k in dead)
            games.Remove(k);
        return dead;
    }

    private static readonly List<string> NoneExpired = new(); // (always empty: callers only read it)

    public static int Run(string[] args)
    {
        var port = args.SkipWhile(a => a != "--master").Skip(1).Select(a => ushort.TryParse(a, out var p) ? p : (ushort)0).FirstOrDefault(p => p > 0);
        if (port == 0)
            port = DefaultPort;
        MasterConsole.Attach(); // a console window to watch it in (tools\RunServerList.bat); the log is serverlist.log
        Console.WriteLine($"[master] Call of Slop server list on UDP {port}. Close this window to stop it.");
        Console.WriteLine("[master] Friends add this PC's public address (shown below once known) in Settings > Multiplayer > Server lists.");
        using var upnp = UpnpMapping.TryCreate(port, Console.WriteLine);
        using var net = NetPeer.Host(port, 1);
        var options = new Options { Rank = 0, LanIp = UpnpMapping.GetLocalIPv4()?.ToString() };
        if (upnp is { ExternalIp: { } ext, BehindSecondNat: false })
            options.PublicIp = ext;
        if (!args.Any(a => a.Equals("--no-stun", StringComparison.OrdinalIgnoreCase)))
            _ = Task.Run(async () =>
            {
                if (await Stun.QueryAsync(Stun.DefaultServers, TimeSpan.FromSeconds(3)) is { } ep)
                {
                    options.PublicIp = ep.Address.ToString();
                    Console.WriteLine($"[master] public address {ep.Address} (STUN): tell friends to add {ep.Address}{(port == DefaultPort ? "" : $":{port}")} as a server list");
                }
            });
        var games = new Dictionary<string, (string Ip, ushort Port, DateTime Seen)>();
        var lastLog = DateTime.MinValue;
        while (true)
        {
            Pump(net, games, options);
            foreach (var dead in Expire(games))
                Console.WriteLine($"[master] - {dead}");
            if (DateTime.UtcNow - lastLog > TimeSpan.FromMinutes(5))
            {
                lastLog = DateTime.UtcNow;
                Console.WriteLine($"[master] {games.Count} game(s) listed");
            }
            Thread.Sleep(20);
        }
    }
}
