# Steam: the platform layer (Phases 1 and 2 of the Steam migration)

The plan (which features, in which order) is the project doc `claude/steam-integration.md`. This file says what Phases 1
and 2 built and how the later phases (stats, Workshop, Steam Input) plug into it.

## The App ID

Call of Slop's Steam App ID is **1750920** (`SteamPlatform.CallOfSlopAppId`; from the Steamworks partner site, not a
secret). A Steam build is `dotnet publish ... -p:SteamAppId=1750920`; a developer's run puts `1750920` in
`steam_appid.txt` next to the exe (uncommitted). A plain build never assumes it, and a bare `--steam` uses Valve's
Spacewar placeholder 480, only ever for local testing.

## Where

| What | Where |
| --- | --- |
| The facade the game calls | `Source/Platform/Steam/SteamPlatform.cs` (`SteamPlatform`, static, partial) |
| Names and avatars | `Source/Platform/Steam/SteamFriends.cs` (`SteamPlatform.Friends`), `SteamAvatars.cs` (GL textures for the menus) |
| The backend interface and the real one | `Source/Platform/Steam/SteamBackend.cs` (`ISteamBackend`, `SteamworksBackend` over Steamworks.NET) |
| The recorded backend (tests) | `Source/Platform/Steam/RecordedSteamBackend.cs` |
| The app hooks | `Source/App/Program.Steam.cs` (init after the save folder, `RunCallbacks` once a frame, the overlay, shutdown, the display name) |
| Steam ids on the wire | `Source/Game/GameSteam.cs` (`Msg.SteamIds`, protocol 76) |
| The self-test | `Source/App/SelfTestSteam.cs`, section "Steam platform" |
| The binding | NuGet `Steamworks.NET.AnyCPU` 2026.165.3 (never 2026.164.3); natives in `runtimes/<rid>/native`, flat next to the exe in a published package |

## Steam is optional

Nothing touches the Steamworks binding unless `SteamPlatform.Plan` says to start Steam. The decision, in order:

1. `--no-steam`: off.
2. A Steam build: `dotnet build|publish -p:SteamAppId=1750920` stamps the App ID into the assembly (`AssemblyMetadata`),
   `SteamPlatform.IsSteamBuild` is true, the GitHub updater is off, and `SteamAPI_RestartAppIfNecessary` relaunches the
   game through Steam when it was started directly (Main returns 0 without a window).
3. `--steam` (the Spacewar placeholder App ID 480, marked as such in the log: local testing only) or `--steam=NNN`. The
   id goes to Steam through the `SteamAppId` / `SteamGameId` environment variables; no file is written. Never relaunches.
4. `steam_appid.txt` next to the program (the developer's file: `.gitignore`, `tools/package.ps1` and
   `UpdateVerifyCli.PersonalFiles` keep it out of every package). Never relaunches.
5. The Steam client launched the game (its `SteamAppId` environment variable).
6. Else off: every GitHub/tester build, LAN game and self-test.

`SteamPlatform.State` is `Off`, `Unavailable` (asked for, but Steam is not running, the native library is missing, or
the App ID is wrong: `Reason` says which, the game carries on) or `Running`. `Available` is the one check features use.

## The API (Phase 1)

```
SteamPlatform.Init(args, exeFolder)      // Program.Steam.cs calls it once; false = not running (Reason says why)
SteamPlatform.RestartRequested           // quit now: Steam is relaunching the game
SteamPlatform.RunCallbacks()             // once a frame (RunLoop and the splash loop)
SteamPlatform.Shutdown()                 // before the window goes
SteamPlatform.Available / State / Reason / StartPlan / AppId / IsSteamBuild / BuildAppId
SteamPlatform.LocalSteamId / PersonaName
SteamPlatform.OverlayActive, event OverlayChanged(bool)
SteamPlatform.Friends.NameOf(id) / AvatarOf(id)   // cached; "" / null until Steam has it, then event Friends.Changed(id)
SteamAvatars.Texture(id)                 // a GL texture for the menus (render thread only), 0 while unknown
SteamPlatform.Backend / UseBackend(ISteamBackend)  // the self-test swaps in RecordedSteamBackend
SteamPlatform.NativeLibraryPath()        // where steam_api is, null when it is missing
```

`Settings.SteamName` ("Use my Steam name", on) makes the persona name the display name (`Program.DisplayName()`),
cleaned to the game's ASCII name rules. The pre-match lobby draws a Steam player's avatar before their name when both
sides are on Steam (`LobbyRoomView.Row.SteamId`, `UiLobbyRoom.cs`).

## Transport, lobbies, invites, Rich Presence and auth (Phase 2)

Protocol **75** (`Msg.SteamAuth` at the end of `Msg`). A game without Steam sends nothing new and runs as before.

| What | Where |
| --- | --- |
| The transport interface | `Source/Net/NetTransport.cs` (`INetTransport`, `NetConnectionStats`, `NetLane` + `NetLanes`) |
| RakNet (UDP, as always) | `Source/Net/RakNetTransport.cs` |
| Steam sockets, and a host on both | `Source/Net/SteamTransport.cs` (`SteamTransport`, `DualTransport`) |
| The facade the game calls | `NetPeer` in `Source/Net/Net.cs` (`Host`/`Join` are RakNet; `new NetPeer(INetTransport)` for the rest; `Discovery` stays RakNet) |
| The backend API (sockets, lobbies, presence, auth) | `Source/Platform/Steam/SteamBackend.Net.cs`, recorded in `RecordedSteamBackend.Net.cs` (`RecordedSteamWorld`: several users in one process) |
| Lobbies and the lobby list | `Source/Platform/Steam/SteamLobbies.cs` (`SteamPlatform.Lobby`) |
| Rich Presence | `Source/Platform/Steam/SteamPresence.cs` (`SteamPlatform.Presence`); tokens in `docs/steam/richpresence_english.txt` |
| Auth tickets and sessions | `Source/Platform/Steam/SteamAuth.cs` (`SteamPlatform.Auth`); the game side `Source/Game/GameSteamAuth.cs` |
| The app hooks | `Source/App/Program.SteamNet.cs` (hosting, joining through a lobby, +connect_lobby, presence, bans) |
| Connection stats | `Source/Game/GameNetStats.cs` (`Game.ConnectionStats`, `AllConnectionStats`, `TransportKind`) |
| The UI | the server browser lists lobbies (`ServerBrowser` + `Lobby.cs`), "Steam lobby" under HOST A GAME, "Invite Steam friends" in the lobby room header, the pause menu's HOST page (kick / ban / unban) |
| The self-test | `Source/App/SelfTestSteamTransport.cs`, section "Steam transport" |

**Transport.** `NetPeer` is the game's one door to the network; underneath, an `INetTransport` moves the bytes. Connections
are `RakNetGUID`s whatever the transport (RakNet's own ids; over Steam the peer's Steam ID in `G`), and connection events
arrive as RakNet's one-byte system packets (`NewIncomingConnection`, `DisconnectionNotification`, ...), so `Game.OnPacket`
and the ~80 files that send are unchanged. The Steam transport is ISteamNetworkingSockets P2P: the host
`CreateListenSocketP2P` on virtual port 0 with one poll group, a client `ConnectP2P` to the host's Steam ID,
`InitRelayNetworkAccess` at start so the relay (SDR) is warm; a direct route when NAT allows, Valve's relays otherwise
(no UPnP, STUN or port forwarding). `PacketReliability` maps to send flags (reliable; unreliable + NoNagle) and to three
lanes (`ConfigureConnectionLanes`): 0 reliable gameplay, 1 unreliable snapshots (highest priority), 2 chat / voice / the
big join-time states (`NetLanes.For`). Messages stay under Steam's 512 KB limit by a mile (the snapshot splitting is
unchanged). A host on Steam listens both ways (`DualTransport`): RakNet on the game port for LAN, invite codes and server
lists, Steam sockets for friends on Steam; sends are routed by which side knows the peer. The self-tests keep RakNet over
loopback; "Steam transport" drives the Steam one on the recorded backend (two or three Steam users in one process).

**Lobbies and joining.** A host on Steam makes a lobby (Settings `steam_lobby`: 2 public, 1 friends, 0 invite only; under
HOST A GAME) whose data is `game=callofslop`, `protocol`, `host` (the host's Steam ID) and `info` (the `ServerInfo`
encoded, refreshed every second with the advert). The browser asks for public lobbies of this game and protocol on each
refresh and shows them as rows (`steam:lobby:ID` addresses, source "Steam", merged with the same game's LAN row by hosting
id). Every Steam join goes through the lobby: a browser row, an overlay invite (`GameLobbyJoinRequested_t`), "Join game"
on a friend (`GameRichPresenceJoinRequested_t`) and `+connect_lobby ID` on the command line (Steam passes it when the game
is not running) all call `SteamPlatform.Lobby.Join`; once the lobby names the host, `StartSession(Join)` connects over
`SteamTransport` to that Steam ID. No host migration: the lobby ends with the host. The host's "Invite Steam friends" opens
the overlay's invite dialog.

**Rich Presence.** `steam_display` is a token (`#Status_Menu`, `#Status_SinglePlayer`, `#Status_Lobby`, `#Status_Playing`,
`#Status_Zombies`) with `mode`, `map`, `players`, `max`, `round` substitutions; `connect` is `+connect_lobby ID` while in
a lobby (the friends-list "Join game"), `steam_player_group` groups the party. The tokens live in
`docs/steam/richpresence_english.txt`, which the developer uploads (below); the self-test checks the file defines every
token the game sets.

**Auth and bans.** Over the Steam transport the host fills `Player.SteamId` from the connection itself. A client on Steam,
once it knows the host's Steam ID (the connection, or player 0 in the Steam id table), gets a session ticket for that
host (`GetAuthSessionTicket`) and sends it (`Msg.SteamAuth` kind 0). A host on Steam begins an auth session with it;
Steam's verdict (`ValidateAuthTicketResponse_t`) kicks the player with the reason on anything but OK (kind 1, then the
connection closes). A claimed id that is not the connection's is refused. `Game.KickPlayer / KickSteam / BanSteam`;
bans persist in Settings `steam_bans` and are refused at the door (and the HOST page lists players with Kick / Ban and
the bans with unban).

**Connection quality.** `Game.ConnectionStats(playerId, out NetConnectionStats)` (a client: the host's connection) and
`AllConnectionStats` for a host: ping, local / remote quality, bytes and packets per second, pending and unacked bytes,
queue time, relayed. RakNet fills the ping only; Steam everything (`GetConnectionRealTimeStatus`). The HUD's "Show
network stats" adds a Steam line; feat/perf-menu's F9 window reads the same calls.

## Developer steps for Phase 2 (Steamworks site)

1. **Rich Presence localisation.** Steamworks > your app > Edit Steamworks Settings > Community > Rich Presence
   (Localization), upload `docs/steam/richpresence_english.txt` (English), then Publish. Without it friends see no status
   and the "Join game" button still works (it comes from the `connect` key).
2. **Two-machine test with a friend** (both on this build, both signed in to Steam, each with `steam_appid.txt` =
   `1750920` next to the exe until the depot ships): see "The manual test" below, Phase 2 part.

## Adding a phase

- Add the Steam calls the phase needs to `ISteamBackend`, implement them in `SteamworksBackend` (wrap every call in
  `Try`, never let a Steam failure throw into the game) and in `RecordedSteamBackend` (record the call, answer from
  fields the test sets). Keep every member safe before `Init` (false / null / empty).
- Put the game-facing side in a new partial file `SteamPlatform.<Phase>.cs` (like `SteamFriends.cs`), gated on
  `Available`, and a `Program.Steam*.cs` / `Game*.cs` partial for the hooks.
- Transport (Phase 2): a transport interface under `NetPeer` (`Source/Net/Net.cs`), RakNet and Steam sockets; the
  connection's identity fills `Player.SteamId` the way `Msg.SteamIds` does now. Lobbies, Rich Presence and invites:
  `ISteamBackend` members plus callbacks exposed as events, as `OverlayActivated` is.
- Stats and achievements (Phase 3): store at match end from `Game`, through the backend; the recorded backend keeps
  the stored values so the self-test can read them back.
- Workshop (Phase 5): subscribed items scanned like a pack; `RequestUserInformation`-style request-then-event pattern.
- Steam Input (Phase 6): action sets mirroring `Keybinds`; poll in `UpdateLocalPlayer`.
- Every phase: a section in `SelfTest.cs` driving the layer on the recorded backend, and nothing that runs when
  `Available` is false, so the suite stays green without Steam.

## The manual test (developer, Windows PC with Steam installed)

1. Build the branch: `dotnet build -c Release` (or `Start.bat`, which also launches it; close it again).
2. Write the App ID into a file next to the exe: in `bin\Release\net10.0\` make `steam_appid.txt` holding one line,
   `1750920`. (Git ignores it. `480` instead means Spacewar: Steam then shows you playing Spacewar.)
3. Start Steam and sign in. Start the game (`bin\Release\net10.0\CallOfSlop.exe`, or `Start.bat`).
4. Expected, in order:
   - `callofslop.log` (in the save folder, `%APPDATA%\CallOfSlop`) has `[steam] starting: steam_appid.txt: App ID 1750920`
     then `[steam] running: App ID 1750920, <your Steam name> (<your Steam ID>), overlay on`. An `[steam] unavailable:`
     line names the problem instead (Steam not running; the App ID not in your account: Steam then refuses).
   - Main menu > Settings > Player shows "Use my Steam name" ticked and `Steam: <your Steam name> (App ID 1750920)`.
   - Host a single-player match, then Shift+Tab: the overlay opens, the mouse is free, and the pause menu is up when
     the overlay closes; Resume carries on.
   - With a second PC on Steam joining (invite code or LAN address, both on this build), the pre-match lobby's player
     list shows each Steam player's avatar before their name, and their Steam name.
   - Quit: the log ends with `[steam] shut down`.
5. Report the `[steam]` lines if anything differs.

Phase 2, with a friend on another PC (both on Steam, this build):
6. Host a game (Multiplayer > HOST A GAME, "Steam lobby: Public" or "Friends"). The log says `[steam] hosting over RakNet and
   Steam sockets (<your id>)` then `[steam] lobby <id> made (Public)`. The lobby room header has "Invite Steam friends".
7. Your friend: the server browser shows your game with "Steam" after its name and "Steam" in the Ping column (public), or
   they accept your overlay invite, or click "Join game" on you in their friends list. Their log: `[steam] connecting to
   <your id> over Steam sockets`; yours: `Player N connected`, then `Steam verified <name>` in the feed.
8. Play a round. With "Show network stats" on, the friend's HUD shows `Steam relay|direct   quality ...`.
9. Pause > HOST: your friend is listed with Kick and Ban. Kick them: they see "Removed by the host: kicked by the host".
10. Quit the game, start it from Steam's friends list "Join game" on your friend's hosted game: it starts straight into
    their lobby (`+connect_lobby`).
