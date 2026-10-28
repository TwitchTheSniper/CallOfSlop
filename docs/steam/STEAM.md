# Steam: the platform layer (Phase 1 of the Steam migration)

The plan (which features, in which order) is the project doc `claude/steam-integration.md`. This file says what Phase 1
built and how the later phases (transport, lobbies, stats, Workshop, Steam Input) plug into it.

## Where

| What | Where |
| --- | --- |
| The facade the game calls | `Source/Platform/Steam/SteamPlatform.cs` (`SteamPlatform`, static, partial) |
| Names and avatars | `Source/Platform/Steam/SteamFriends.cs` (`SteamPlatform.Friends`), `SteamAvatars.cs` (GL textures for the menus) |
| The backend interface and the real one | `Source/Platform/Steam/SteamBackend.cs` (`ISteamBackend`, `SteamworksBackend` over Steamworks.NET) |
| The recorded backend (tests) | `Source/Platform/Steam/RecordedSteamBackend.cs` |
| The app hooks | `Source/App/Program.Steam.cs` (init after the save folder, `RunCallbacks` once a frame, the overlay, shutdown, the display name) |
| Steam ids on the wire | `Source/Game/GameSteam.cs` (`Msg.SteamIds`, protocol 74) |
| The self-test | `Source/App/SelfTestSteam.cs`, section "Steam platform" |
| The binding | NuGet `Steamworks.NET.AnyCPU` 2026.165.3 (never 2026.164.3); natives in `runtimes/<rid>/native`, flat next to the exe in a published package |

## Steam is optional

Nothing touches the Steamworks binding unless `SteamPlatform.Plan` says to start Steam. The decision, in order:

1. `--no-steam`: off.
2. A Steam build: `dotnet build|publish -p:SteamAppId=NNN` stamps the App ID into the assembly (`AssemblyMetadata`),
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

## Developer steps outside the code

The App ID (the Steamworks partner site), `steam_appid.txt` with it for local runs, and a manual run with Steam
installed: start the game with Steam running and `steam_appid.txt` (or `--steam` for Spacewar) and check the log's
`[steam] running:` line, the name in Settings > Player, Shift+Tab (the overlay pauses), and the avatar in a lobby with
a second Steam account.
