# Server discovery: playing with friends without a central server

The question (from the user): can a game be listed without a master server, and if not, can a lightweight list server
ship with the release so that **anyone with the release can play with all their friends easily**, with no paid hosting?

Short answer: a list needs *something* everyone can reach. Without any server, the only things that work are LAN
broadcast and sharing an address directly (an invite code). So the build combines **invite codes** (zero
infrastructure) with a **server list that ships in the game and starts itself** when a host finds none, and spreads
through invites, so a friend group ends up sharing one list without anyone typing addresses.

## What was there before

| Piece | Where | What it does |
|---|---|---|
| LAN broadcast | `ServerBrowser.Refresh` | Unconnected RakNet ping to 255.255.255.255 on the usual game ports; every host answers with its `ServerInfo` (name, map, players, protocol). |
| Master server | `MasterServerApp` (`--master`, UDP 27050) | Hosts send `COHB` heartbeats every 20 s from the game socket; browsers send `COLQ`, get `COLS` (addresses only), then ping each game. One address in Settings, empty by default, so in practice nobody had one. |
| UPnP | `Upnp.cs` | Opens the game's UDP port on the router and reads the router's external address. |
| Join by address | Lobby | IP + port typed by hand. |

## Options considered

### 1. LAN broadcast (exists)
Zero setup, only the local network. Kept as is.

### 2. Invite codes and links (implemented)
The host's public IPv4, game port, protocol version and its own server list's port packed into 8 bytes plus a 2-byte
checksum, written as 16 Crockford base32 characters: `ABCD-EFGH-JKMN-PQRS`, or a link `callofslop://join/ABCD-...`.
- No infrastructure at all; works whenever the host is reachable (UPnP, a forwarded port, or the same LAN).
- The checksum catches typos (a single wrong character slips through 1 time in 65536); look-alikes O/0 and I/L/1 are
  accepted; an invite from another build says "different version (protocol N; yours is M)" instead of timing out.
- Hostnames and IPv6 can't be encoded (the Join box still takes them as plain addresses).
- The link handler is registered per user (HKCU, no admin) **only when the player clicks** "Make invite links open the
  game" in Settings > Multiplayer, and the same place removes it. The link only passes the code, which is validated
  like a typed one.

### 3. Discovering your public address: STUN (implemented)
The router's UPnP answer is often missing (UPnP off) or wrong (carrier-grade NAT: the router's "external" address is
itself private). A minimal RFC 5389 client (`Stun.cs`, .NET sockets only) sends one 20-byte Binding request to two
public STUN servers (Google's and Cloudflare's) and takes the first answer within 3 s. No answer just means "unknown"
(the invite falls back to the UPnP or LAN address). When UPnP and STUN disagree the game warns about carrier-grade NAT.
It can be turned off in Settings > Multiplayer; only the request (a random transaction id) is sent.

### 4. A serverless public list over the BitTorrent DHT (considered, declined by the user)
Hosts could `announce_peer` under an info-hash such as `SHA1("CallOfSlop/<protocol>")` on the global Mainline DHT
(BEP 5), and browsers `get_peers` it, then ping the results. It is the only truly serverless *public* listing, but:
it contacts third-party DHT nodes (bootstrap nodes like router.bittorrent.com), makes every host's IP discoverable by
anyone querying the hash, needs a careful KRPC/bencode client with rate limits, and lookups take seconds. **The user
decided not to implement it**, so there is no DHT code and no DHT traffic in the game.

### 5. NAT traversal
- **UPnP** (exists): fine when the router allows it.
- **Hole punching** needs a rendezvous that both sides can reach to exchange their public endpoints. A host behind NAT
  can't be its own rendezvous (nobody can reach it unsolicited), but any reachable list server could be one. The
  BRUTAL RakNet bindings expose the core `RakPeerInterface` only (Startup, Connect, Ping, AdvertiseSystem, SendTTL,
  GetExternalID...): **no NatPunchthroughClient/Server, NatTypeDetection or UDPProxy plugins**. A hand-made punch
  (both sides pinging each other on cue from the list) is possible with `Ping`/`SendTTL` but was left for later.
- **Relay fallback** (for symmetric NAT / CGNAT) means a server relaying every packet: real bandwidth, i.e. hosting
  costs. Not done; the advice is "let a friend with a normal connection host".

### 6. GitHub (or similar) as a registry
Writing needs an account token, which testers don't have and shouldn't be handed. Rejected.

### 7. A lightweight list server shipped in the release (implemented)
The master logic is tiny (addresses only, no game data) and already in the exe. It now runs three ways:
1. **Automatically inside a host's game** (`HostListing`): when hosting starts, the game probes the player's known
   lists (1.5 s, in the background). If one answers, it registers there. If none does, it starts a list in-process
   (UDP 27050, or the next free of 27050-27059, opened with UPnP like the game port), registers its own game on it,
   and keeps it while hosting. The list goes into the host's invite code and server info.
2. **By hand in the game**: Settings > Multiplayer > "Run a server list for friends on this PC" keeps one running
   while the game is open.
3. **Without the game**: `RunServerList.bat` in the release zip (`CallOfSlop.exe --master [port]`), which opens a
   console window (close it to stop) and logs to `serverlist.log`.

## How a friend group converges on one list

- **Joining spreads the list.** A client that joins by invite code adds the code's list address to its saved lists;
  any join (code, address, LAN, browser) also pings the host once in the background and reads the list from the
  host's `ServerInfo` (a new optional 'L' extension). So after one game together, everyone knows the group's list.
- **One list per group.** Hosts re-probe their lists every 60 s. A list started automatically (rank 1) hands over to
  a better one when it answers: a dedicated or hand-started list (rank 0), or an automatic one with a lower random id.
  A host whose list disappears (the list's host quit) finds none at its next probe and starts its own. The browser
  asks every saved list anyway, and shows each game once, so a temporary second list is invisible to players.
- **Settings > Multiplayer > Server lists** keeps up to 8 addresses (typed or learned; the oldest learned go first).
  A group can also bake an always-on list into its zip: `masters.txt` next to the exe, one `host[:port]` per line.

## Protocol and compatibility

- **No game protocol change** (still 36). The new data is discovery-only and optional:
  - `ServerInfo` (the unconnected pong) gains tagged extensions after the rotation string: `'I'` + a 32-bit hosting
    id (to merge a game found on the LAN and through a list into one row), `'L'` + list port + host name ("" = on
    the host's own address). Older builds stop reading after the rotation.
  - The list reply (`COLS`) gains `"LSID"` + rank byte + 32-bit id after its entries; older parsers stop after the
    entries.
- A list server rewrites loopback/LAN addresses of games registered from its own PC or LAN to its public address
  (STUN/UPnP) when an outside browser asks, so "my game and my list on one PC" works for friends outside.
- Settings: `master=` may hold several comma-separated lists (old single values load as before); new keys
  `share_on_lists` (default on; an old `advertise=1` also turns it on), `auto_list` (on), `run_list` (off),
  `stun` (on).
- The browser now shows games from other builds greyed with their protocol instead of hiding them.

## Privacy and safety

- What anyone with a list's address can see: each listed game's IP and port plus the `ServerInfo` that LAN broadcasts
  already carried (name, map, mode, players). Nothing else is stored; lists are run by players, never by a company.
  Hosting can opt out ("Share on our server lists" off): invite codes still work.
- STUN sends a random 20-byte request to Google's or Cloudflare's STUN server; opt-out in Settings.
- Never blocks the UI: probes, STUN, DNS and UPnP run on background tasks; host names are resolved in the background.
- Caps: a list keeps at most 512 games, answers at most 4 queries per second per address, replies with at most 60;
  the browser keeps at most 256 games and pings at most 64 per list reply. Every incoming packet (pong extensions,
  list replies, STUN answers, invite codes, links) is length-checked and fuzzed in the self-test; text is cleaned.

## Code map

| File | What |
|---|---|
| `Source/Net/InviteCode.cs` | Invite codec, the Join box parser, version message. |
| `Source/Net/Stun.cs` | STUN Binding client and parser. |
| `Source/Net/ListServer.cs` | In-process list server, `PublicAddress`, `ListProbe`, `Heartbeats`, `HostListing`, `ListLearner`, the `--master` console. |
| `Source/Net/ServerBrowser.cs` | `ServerInfo` extensions, browser over many lists with merge, master logic (`AddressFor`, rate limits, list id). |
| `Source/Net/UrlProtocol.cs` | Opt-in `callofslop://` registration (HKCU). |
| `Source/App/Program.Listing.cs` | Host addressing, the invite line, host listing, learning lists, invite links on the command line. |
| `Source/UI/UiInvite.cs` | Lobby room invite buttons, Host tab options, Settings > Multiplayer. |
| `Source/App/SelfTestListing.cs` | Self-test sections "Invite codes", "STUN", "Server lists", "Automatic server list" (loopback only). |
| `tools/RunServerList.bat`, `tools/masters.txt` | Shipped in the release zip by `tools/Package.bat`. |
