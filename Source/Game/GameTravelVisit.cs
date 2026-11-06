using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Free travel, a traveller's side (docs/space/FREE-TRAVEL.md, route 1): when the host says a ship this player rides has
/// landed on a planetoid it runs (GameTravel.cs), this PC opens a client game for that planetoid (<see cref="Visit"/>,
/// drawn and steered as ever, Program.Travel.cs) that joins it over <see cref="RelayTransport"/>: its packets go up the
/// connection to the host (the link, which stays open behind it for space, chat and who is where), tagged with the
/// planetoid's number. When the ship is back in the host's space, the visit closes and the link plays again.
/// </summary>
public sealed partial class Game
{
    /// <summary>A link to the host: the planetoid's game this link carries (null: none).</summary>
    public RelayTransport? Relay;

    /// <summary>A traveller's game for a planetoid: the game connected to the host that it runs beside (null: an ordinary game).</summary>
    public Game? Link { get; private set; }

    /// <summary>A traveller's game for a planetoid: which one.</summary>
    public TravelVisit? Visit { get; private set; }

    /// <summary>A link: the host says to join this planetoid (<see cref="TravelStep"/> opens the visit).</summary>
    public TravelVisit? PendingVisit;

    /// <summary>A link: the host has the traveller back in its space (<see cref="TravelStep"/> closes the visit).</summary>
    public bool PendingHome;

    /// <summary>A planetoid to join: the world, the map, and its number on the host.</summary>
    public sealed class TravelVisit
    {
        public string World = "", Map = "";
        public byte Place;
    }

    private void OnTravelPacket(RakNetGUID sender, ref PacketReader r)
    {
        var purpose = r.U8();
        if (IsServer)
            return;
        if (purpose == PurposeVisit)
        {
            var sr = new SaveReader(r.Take(r.Remaining).ToArray()); // (written with a SaveWriter: GameTravel.cs PlaceVisit)
            TravelVisit visit;
            try
            {
                visit = new TravelVisit { World = sr.Str(), Map = sr.Str(), Place = sr.U8() };
            }
            catch (WorldFileException ex)
            {
                Console.WriteLine($"[travel] a bad visit message from the host: {ex.Message}");
                return;
            }
            PendingVisit = visit;
            Console.WriteLine($"[travel] the host sends this game to {visit.Map} (place {visit.Place})");
        }
        else if (purpose == PurposeHome)
            PendingHome = true;
    }

    private void OnRelayPacket(RakNetGUID sender, ref PacketReader r)
    {
        var kind = r.U8();
        var other = r.U8();
        if (IsServer)
        {
            OnRelayHost(sender, kind, other, ref r); // (GameTravel.cs)
            return;
        }
        if (Relay is not { } t)
            return;
        if (kind == RelayData)
            t.Deliver(other, r.Take(r.Remaining));
        else if (kind == RelayOpen)
            t.Opened(other);
        else if (kind == RelayShut)
            t.Closed(other);
    }

    /// <summary>A link: a packet from the planetoid's game it carries, up to the host.</summary>
    internal void RelaySend(byte to, ReadOnlySpan<byte> data, PacketReliability how)
    {
        _relayW.Begin(Msg.Relay).U8(RelayData).U8(to).U8((byte)how);
        _relayW.Reserve(data.Length);
        _relayW.Bytes(data);
        Net.SendToServer(_relayW.Span, how);
    }

    /// <summary>A link: the game it carries opened or closed its connection to planetoid <paramref name="other"/>.</summary>
    internal void RelayControl(byte kind, byte other)
    {
        _relayCtl.Begin(Msg.Relay).U8(kind).U8(other);
        Net.SendToServer(_relayCtl.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// The centre line while the local player isn't in play because of free travel: landing on a planetoid (until they
    /// sit in their ship there) or away on one (the link, until the ship is back). Null otherwise. Made once per map: no
    /// allocation a frame.
    /// </summary>
    public string? TravelBanner
    {
        get
        {
            if (Visit?.Map is { } map)
            {
                if (!ReferenceEquals(_bannerFor, map))
                    (_bannerFor, _banner) = (map, $"Landing on {map}...");
                return _banner;
            }
            if (IsServer || Local is not { AwayOn: { } away })
                return null;
            if (!ReferenceEquals(_awayFor, away))
                (_awayFor, _awayBanner) = (away, $"On {away}...");
            return _awayBanner;
        }
    }

    private string? _banner, _bannerFor, _awayBanner, _awayFor;

    /// <summary>
    /// A traveller's PC, after the games' updates: opens a visit to a planetoid the host runs, or goes back to the link.
    /// <paramref name="here"/> is the planetoid's game, if any; returns the game to play now (the link, or a new or the
    /// same one). Program.Travel.cs and the dev check run this.
    /// </summary>
    public static Game TravelStep(Game link, Game? here, out string? said)
    {
        said = null;
        if (link.PendingVisit is { } visit)
        {
            link.PendingVisit = null;
            link.PendingHome = false;
            here?.Dispose();
            return OpenVisitor(link, visit);
        }
        if (here == null)
        {
            link.PendingHome = false;
            return link;
        }
        if (link.PendingHome || here.Disconnected)
        {
            said = here.Disconnected && !link.PendingHome ? $"the visit to {here.Visit?.Map} ended ({here.Status})" : null;
            link.PendingHome = false;
            here.Dispose();
            World.Enter(link.Place);
            return link;
        }
        if (here.Local is { Alive: false } && !here.RespawnRequested && here.Phase == MatchPhase.Playing)
            here.RequestRespawn(); // (the traveller sits in their ship as it comes in: WorldOnSpawn on the planetoid's game)
        return here;
    }

    /// <summary>A traveller's game: a client of the planetoid's game on the host (<see cref="RelayTransport"/>).</summary>
    public static Game OpenVisitor(Game link, TravelVisit visit)
    {
        var g = new Game(new NetPeer(RelayTransport.Joining(link, visit.Place)), link.Audio, link.LocalName, link.ChosenPrimary, link.ChosenSecondary)
        {
            LocalWorldKey = link.LocalWorldKey,
            Progress = link.Progress,
            ChosenHat = link.ChosenHat,
            ChosenLook = link.ChosenLook,
        };
        g.Link = link;
        g.Visit = visit;
        Console.WriteLine($"[travel] joining {visit.Map} (place {visit.Place}) on the host");
        return g;
    }
}
