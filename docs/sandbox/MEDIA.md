# Video Screens and Music Players

Written 2026-11-05. The parts play YouTube links and the game's own tracks in the world: a picture on a screen, and sound that fades with
distance. A link needs the Windows game with its Browser folder. A game track needs nothing and plays on every platform.

## The parts

`tools/constructs/media_parts.py` writes `Constructs/Parts/Media`, then `tools/constructs/assembling.py` writes their recipes.

| Part | Picture | Seen to | Heard to | Full level to |
| --- | --- | --- | --- | --- |
| Music Player | 356 x 200 page, the top of a screen filling its face, with buttons under it | 12 m | 50 m | 5 m |
| Video Screen | 1280 x 720 | 25 m | 40 m | 4 m |
| Medium Video Screen | 1280 x 720 | 60 m | 80 m | 8 m |
| Large Video Screen | 1280 x 720 | 150 m | 160 m | 16 m |

All of them run from the configure tool's panel (`Source/UI/UiSandboxMedia.cs`): paste a link, add one of the game's tracks, reorder or remove lines,
play, pause, stop, previous and next. Volume and Loop are ordinary settings. A first link plays as soon as it is added.

A part file's `"media"` key holds `hear`, `near` and, for a small page, `width` and `height`. The Music Player has a small screen rather than none, because
YouTube's terms don't allow playing a video's sound with its player out of sight, and the player must be at least 200 x 200.

## Who holds what

- **The server holds the word**: `CMedia` on the part (`ConstructMedia.cs`): the list, the line, Stopped/Playing/Paused, the tick a track started at, and a `Seq` that
  goes up when a track starts. Changes come in as `CActKind.Media`, under the configure tool's rule: contested, permission and reach. `Ended` is the exception: it is a report, not a player's act.
- **Each peer plays it for itself** (`MediaDirector`): every 0.25 s it finds parts playing within earshot, opens pages for the nearest links and keeps them on the server's clock.
  A joiner gets the CBody, opens the page at that clock's time, and seeks if it is 2.5 s off. Nothing about pictures or sound goes on the wire.
- **Ends.** The server knows a game track's length and moves on by itself. Only a browser knows how long a video is, so the first peer to see the end says so; the `Seq` makes a second report
  do nothing. A video that can't play is reported only by whoever started it, and only with more lines to go: one friend's region block doesn't skip it for everyone.
- **Lists are at most 32 lines** (`ConstructMedia.MaxList`). They ride in CBody's tail, a blueprint step's `"media"` list and a world save's settings (`media.0`, `media.1`...: three characters a float, as sign text does,
  so an older reader skips them). What is playing isn't saved: a world comes up stopped.

## What a link becomes

`MediaEntry` is the only form kept: `yt:` and an 11-character id, or `game:` and a track's name. A pasted URL is taken apart on the client, and only youtube.com,
youtu.be and youtube-nocookie.com hosts give an id. Every way in (the server's handler, CBody on a client, a blueprint, a world file) goes through `MediaEntry.Clean`. The browser checks the id once more and builds the page from it alone, so
no text from another player can point anyone's browser anywhere. A playlist link adds its one video.

## The browser

`tools/browser` is an exe of its own, CefSharp's offscreen build of stock CEF, published to `Browser/` next to the game by `tools/package.ps1` and, once, by `Start.bat`. CI's build and self-test jobs never touch it. The game's process never loads
CEF: `MediaBrowser` starts it for the first page and quits it 20 s after the last. Before that the game has paid one file check. If it fails or crashes, `MediaBrowser.Problem` says why on the panel and the look
line, it gets three goes a session, and game tracks don't notice. Self-tests and `--smoketest` leave `MediaBrowser.Allowed` off and never start one. CefSharp needs the Visual C++ 2022 runtime; a PC without one gets "the browser closed" on the panel, and only links are affected.

- **At most 4 pages** (`MediaDirector.MaxPages`). Others say so on their panels. A page is 720p VP9 decoded in software, so more would be felt on the CPU, though in another process.
- **Commands** go in on stdin, **reports** come out on stdout, a line each, on threads of their own. Stdin closing, which is also what a game crash looks like, ends the browser; it has 5 s to close or it exits anyway.
- **Pictures and sound** go through a shared memory block per page (`MediaBlock.cs`, `tools/browser/SharedBlock.cs`: keep the two layouts the same). Three frames, so a frame is written beside the one being read. No side waits for the other and the
  game allocates nothing. `VideoFeeds` copies at most one frame a render, and only for a page a panel asked for, so a screen behind you or out of sight costs no picture.
- **The page.** YouTube's official iframe player, from `youtube-nocookie.com`, in a small page the browser serves itself at `https://callofslop.invalid/player.html`. It needs a real https page: since 2025 the player refuses to run without a Referer
  (error 153), which rules out `file:` and `data:` URLs. Top-level navigation away is refused, popups are closed, no input reaches the page. Nothing is kept on disk. Its picture is shown as it comes, with nothing drawn over it, adverts included, unless this viewer turned on ad blocking.

## Hands on a screen

`Program.SandboxPointer.cs`. With Hands out and empty, in use reach, the crosshair on a live page's picture is a mouse in the browser, with a cursor drawn on the screen. Hover shows YouTube's controls, LMB clicks and drags, and the wheel scrolls.
RMB takes the mouse off that part until the crosshair leaves it. F does what it always did, because the mouse never reads it. It works on a loose screen too: LMB on the picture goes to the page, and to pick one up, use its case or border, or RMB first. The mouse doesn't type: no key reaches the page.

- **Events** go through a small ring in the shared block's header (`MediaBlock.Mouse`), read every 8 ms by a thread on the page, so a moving crosshair allocates nothing.
- **The page can't leave.** Popups are closed, so "watch on YouTube" and the logo open nothing. In the top frame nothing but the page's own address loads, and a click inside a frame can only go to `/embed/`. Navigation without a gesture stays free, so adverts load as usual.
- **Who may.** The mouse is off for anyone the configure rule refuses: they see the picture and the reason on the look line, and clicks do nothing. The server checks every result again.
- **Everyone follows.** For 3 s after a click or wheel on this peer the page leads (`MediaDirector.FromPage`): a pause, a resume or a seek it reports goes to the server as a `MediaOp`, and from there to every peer. A seek also raises `CMedia.Mark`, so others seek at once. After that the server leads again.
- **Volume stays each viewer's own.** From the first click the page stops resetting the player's volume and mute, so the slider sticks, on that PC only. This only applies to the volume inside the player: the part's Volume is shared.

- **One pose.** The cursor comes from the last picture drawn: the world pass records its camera and each panel's drawn pose, and the spot is that ray on that panel (`PanelRender.DrawnSpot`). Input's own poses are a tick apart from those at speed on a host, so they only pick the panel first aimed at; staying on it asks the picture.

A Music Player's picture takes the mouse too. Its buttons are panel buttons, and work on game tracks with no browser.

## Sound

`Audio.Media.cs`: five streamed mono sources on their own thread, one a page and one for a game track, made when something first plays; the thread stops after 5 s of nothing. Mono because OpenAL only places mono: CEF's stereo is mixed down in the browser. They are positioned at the part
every frame, faded by OpenAL's inverse distance from `near` to `hear` like any other sound (the last 20% of `hear` is faded to nothing as well, so a page going doesn't cut off a sound still heard), and pass through the Media mixer bus, the part's Volume and Settings > Audio > Screens volume.
CEF's audio handler takes the sound away from the speakers, so there is no second copy from the browser. Settings > Audio > Play other players' videos, off, hides links started by anyone else on this PC.

**The game's own tracks** use the same voice path with one `MusicRenderer` on the sound thread (the menu's music player's tracks: no frenzy lines, they need a match). The music CPU rule is at most 1.5x the baseline, and one more render would be 2x, so there is only ever one, the nearest, and it holds silently (the panel
keeps its line) while the music thread renders the lobby's decks or the kill frenzy. A Sandbox world in play has neither, so a track plays as one baseline render. A track starts on a bar line, so peers agree to the bar, not the sample.

CefSharp's offscreen settings start with `--mute-audio`, so the helper calls `EnableAudio()`: without it no output exists, nothing is audible, and the handler is never asked. CEF only captures what is audible: while a page is muted the audio handler isn't even asked (tried on stock CEF: nothing until `muted` went false, then sound within moments). So the page unmutes the player and sets its volume whenever it reports, once a second, and clears CEF's own mute once.

### Finding where sound stops

The hops log to `callofslop.log`, each on a 5 s throttle while something plays; CEF's own warnings go to `slopbrowser.log` beside it. A page playing for 6 s with no sound stream also logs `playing with no sound stream` and what YouTube's video element says (`video element: muted ... audio bytes ...`).

| Hop | Line starts | Right | If it's wrong |
| --- | --- | --- | --- |
| 0 | `[media] browser: mute-audio is` | `off` | CEF has no sound output at all |
| 1 | `[media] browser: page 0: player` | `false 100` after at most one `true` line | The player stays muted |
| 2 | `[media] browser: page 0: audio handler asked` | Once, soon after playing | CEF never saw the page as audible |
| 3 | `[media] browser: page 0: audio stream started` | 2 channels, 44100 Hz | The stream didn't open (`audio stream error` says why) |
| 4 | `[media] browser: page 0: audio packets` | Peak above 0 | Peak 0: the page is silent |
| 5 | `[media] sound voice 0 (hop 5)` | Ring written and read both rising, peak above 0 | The game isn't reading the ring |
| 6 | Same line, `OpenAL (hop 6)` | `Playing`, gain above 0 | The source is stopped or silent |
| 7 | `[media] level voice 0 (hop 7)` | Every factor above 0 | That factor is the zero |

## Pipe Boxes

The Clan MacSlop's Music Player (`tools/constructs/highland_parts.py`, `Constructs/Parts/Highland`): a box with the Music Player's small screen and
a set of bagpipes on its lid, heard to 150 m (full level to 10 m), with Loop on by default and no power needed (it has lungs). Its part file's
`"media"` key has `"pipes": true`, and that is all that differs: it plays links, game tracks and speakers as any Music Player does.

**It strikes up by itself** (`ConstructPipes.cs`, once a second on the server, beside `ConstructMedia.Tick`): when its construct gets a driver
with the engine running and it is stopped, it plays, from a line the clock picks so a convoy's boxes don't start together; an empty list is
filled with every pipe tune first (docs/audio/MUSIC.md, "Pipe tunes"). It goes on for 20 s after the driver leaves or the engine stops, then
stops. A player may stop or change it at the panel as usual; it strikes up again the next time someone takes the wheel. Only the server's word
changes (`CMedia.PipesDriven`, `PipesIdle` are the server's own, never sent or saved), so nothing new goes on the wire.

As for every game track, a peer renders only the nearest one, so in a convoy you hear the lead piper. Every Highland vehicle the vehicle kit
builds has one (docs/vehiclekit/VEHICLEKIT.md, "The Highland style"). A speaker on a data cable carries it further, as for a Music Player.

## Browser Screens

Browser Screen, Medium and Large are a real Chromium tab on a part. Address bar, Back, Forward, Reload and Close are on the configure panel. Hands work as on a video, and Enter on the picture gives the page the keyboard until Esc (or the crosshair leaves): the game's keys are off meanwhile. It shares the 4 pages, its sound takes the same path, and speakers work. Windows only.

**One page leads, the others follow.** Whoever used the page last leads: a click, wheel or key on a peer's page takes the lead, as does typing an address. Each peer still runs its own page; nothing about a picture goes on the wire.

**Shared:**

- **The address, with its query and fragment,** so a search or a watch page opens the same. Back, Forward, links and in-page changes (pushState, hash) follow, because the leader's address is what goes out. A follower never reports, so its own redirects or language stay on its page. Secret-looking parameters are taken out first (`Source/Media/WebScrub.cs`, compiled into the game and the helper): names with words like token, auth, session, sid, key, code, sig, password, state or jwt, and values shaped like a JWT. This goes by names, so a secret under an innocent name gets through, and a page that needs a stripped parameter won't open the same elsewhere.
- **How far down the page is,** as a share of the main scroller, at most 4 times a second (`CBodyKind.WebState`, so scrolling doesn't send the body). The main scroller is the document's, or failing that a likely main element.
- **The page's main video or audio:** play, pause and time, on a clock in `CMedia` that joiners read, with the same drift correction as YouTube links (2.5 s, a seek at most every 5 s). Only elements in the top frame are seen: a player inside another site's frame isn't. An advert playing in the element counts as the element.

**Not shared:** clicks, unless they navigate, typing and logins. Keys never go on the wire, and neither does anything from a form. A page that looks different per viewer, such as a logged-in one, stays different: a shared scroll share then lands somewhere else.

Safety, since the address can come from another player (`tools/browser/WebPage.cs`, its own class, handlers and request context, so none of this touches the locked YouTube page):

- **Other players' pages are off by default** (Settings > Audio > Show other players' web pages). Off, they stay blank with "NAME's browser: hidden". Loading one shows the site this PC's address and runs what a stranger chose. It can be turned off at any time, and the next scan closes them.
- **Only public http and https.** `MediaEntry.FromAddress`, `Clean` on every way in, and `WebPolicy` in the helper refuse everything else: `file:`, `chrome:`, `data:`, `javascript:`, devtools, localhost, private and link-local addresses, bare machine names. The helper applies this to what a page loads too, so a public page can't reach into the viewer's network. A public name that resolves to a private address isn't caught: only the page's literal address is checked.
- **Blocked:** downloads, external programs, file pickers, JavaScript dialogs, HTTP login prompts, every permission (camera, microphone, location, notifications). A popup opens in place if a click asked for it.
- **Nothing kept.** A request context in memory per page and a throwaway folder per run. No passwords or cookies on disk. Keys aren't logged, a page's console goes nowhere, and addresses aren't logged.

## Speakers

Speaker, Medium Speaker and Large Speaker (`ConstructSpeakers.cs`) repeat a Video Screen or Music Player: run a Data cable to one with the linking tool, on the same construct or, as a world link, from another. It's a Data cable and no new link kind, so saves, blueprints and the linking tool are the ones every link has. Cut it and the speaker goes silent at the next scan.

- **One stream, sample-locked.** A speaker has no buffers or stream of its own: its source queues the player's own AL buffers, which come off and go on together (`Audio.MediaPump`). One decode reaches every source, and one mixer reads them all, so they can't drift. A speaker joining late takes the player's queue and sample position inside one deferred update (`MediaJoin`), so it starts on the same sample. `[media] voice 0: 2 speakers on its buffers, worst offset N frames` reports the measured gap every 5 s, and a speaker that a device stall moved by over 6 ms snaps back. At most 8 speakers sound at once, nearest first (`MediaDirector.MaxSpeakers`), across all players.
- **Every device has its own Volume**, 0 to 1, default 0.8 on all of them (Video Screen, Music Player, Speaker), and none scales another. A screen at 0 is silent while its speakers carry the sound: its source keeps playing at no gain, so the group's clock and the page go on.
- **A speaker near keeps its player going.** A page or track opens for a speaker in range even if the player itself is out of earshot, and it counts towards the 4 pages by the speaker's distance.
- **Settings:** Loudness, Range (up to 40, 100 or 250 m by size), Falloff (inverse or linear) and its rate, Pattern (every way or directional), Inner and Outer cone along the part's front, Outer level. 
- **The picture** on the configure panel is drawn from `SpeakerShape.Level`, the function the sound uses, so what it shows is what is heard. The game works distance and cone out itself and gives OpenAL the result, rather than using OpenAL's cone and distance settings, so the picture and the linear curve can't differ from it.

## YouTube, checked and not

Checked in the container with a stock CEF (120, Linux, from NuGet, offscreen), the same standard distribution as the 156 Windows build the game ships:

- `MediaSource.isTypeSupported`: VP9, AV1 and Opus yes; H.264 and AAC no. A VP9/Opus WebM played and an H.264/AAC MP4 failed with `DEMUXER_ERROR_NO_SUPPORTED_STREAMS`.
- The audio handler received 44.1 kHz planar float stereo at the file's level, and paints came through with a picture.

Not checked, because youtube.com can't be reached from the container: that a real video plays. Stock CEF has no H.264 or AAC, so anything YouTube offers only in those won't play, and some live streams may be among them. Stock CEF has no Widevine, so paid films won't.
Uploaders can refuse embedding (error 101 or 150), which the panel says. Ads may run, as on any embed; the clock waits and then seeks.

## Ad blocking

A box on the Video Screen, Music Player and Browser Screen panels, off by default (`media_block_ads`, `media_block_web_ads` in the settings: one for YouTube links, one for Browser Screens). It is each viewer's own: it isn't sent to the server or other peers, and their pages are untouched. Changing it reloads the page, which comes back to the clock, so no restart is needed: turning it off is the way out if it breaks playback.

**The terms risk.** Blocking YouTube's ads breaks YouTube's API terms, which this project otherwise keeps to. The user chose this as an opt-in knowing that. It's a risk to the project's standing with YouTube if it ships, and it's why it is off by default and never on for anyone who didn't tick it.

What it does (`tools/browser/AdFilter.cs`, `adhosts.txt` beside the exe, read when the browser starts):

- **Requests:** a page refuses hosts on the list, and on YouTube's own hosts, paths on the list. It can only refuse a request, so it adds no way out of a page's rules. The list is a few dozen lines, nothing like a real ad blocker's.
- **Adverts that get through:** once a second it hides advert overlays in the player's frame and presses a skip button if there is one, with a real mouse click at its place, since the player ignores a script's. If the button goes between looking and clicking, the click lands on the video and may pause it; the clock then starts it again.

**How well it works, honestly:** expect banners, trackers and overlays to go. Expect in-stream adverts mostly to stay. They come from the same servers as the video, so nothing here can tell them apart, an unskippable one plays in full, and the player's page changes often, which can break the skip button's names without warning. Refusing YouTube's tracking calls may also make the player complain or stall: that's what the off switch is for.
