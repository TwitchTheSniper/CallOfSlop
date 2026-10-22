namespace CallOfSlop;

/// <summary>
/// Where the cosmetic loadout reads the local prestige from. One place, so the hat code doesn't care how
/// prestige is stored (Progression.cs owns it).
/// </summary>
public static class CosmeticPrestige
{
    /// <summary>The prestige (0..<see cref="Cosmetics.MaxPrestige"/>) of a local profile (null: 0).</summary>
    public static int Of(Progress? progress) => 0; // TODO(prestige-hats): progress?.Prestige once Progress.Prestige lands
}

/// <summary>
/// Hats on the network (cosmetic only, nothing else changes):
/// <list type="bullet">
/// <item>client -> server: one byte after the kill streaks in <see cref="Msg.Loadout"/> (older clients send none: no hat);</item>
/// <item>the server keeps a hat only for a person whose reported prestige unlocks it (<see cref="Cosmetics.IsUnlocked"/>),
/// bots always wear none, anything else becomes None;</item>
/// <item>server -> all: a trailer on <see cref="Msg.Roster"/> (count, then id + hat per player), sent again whenever a hat
/// changes, so every client knows every player's <see cref="Player.Hat"/> (the hat renderer draws from it).</item>
/// </list>
/// </summary>
public sealed partial class Game
{
    private HatId _chosenHat;

    /// <summary>Client side of the hat (from the loadout screen). On the host it applies to the local player at once.</summary>
    public HatId ChosenHat
    {
        get => _chosenHat;
        set
        {
            _chosenHat = value;
            if (IsServer && Local is { } me)
                ServerSetHat(me, value);
        }
    }

    /// <summary>Hats from the last roster, for players whose snapshot hasn't arrived yet (client).</summary>
    private readonly Dictionary<byte, HatId> _hats = new();

    /// <summary>The hat player <paramref name="id"/> wears (as far as this side knows; None if unknown).</summary>
    public HatId HatOf(byte id) => Players.TryGetValue(id, out var p) ? p.Hat : _hats.GetValueOrDefault(id);

    /// <summary>The prestige a player reported (server). The host's own comes from its profile.</summary>
    private int ReportedPrestige(Player p) =>
        p.Id == LocalId && IsServer ? CosmeticPrestige.Of(Progress)
        : 0; // TODO(prestige-hats): the prestige this client sent in its Hello

    /// <summary>
    /// The hat the server lets a player wear: bots never wear one, people only a known hat their prestige unlocks;
    /// anything else is None. Pure (checked by the self-test).
    /// </summary>
    public static HatId ValidHat(HatId requested, bool isBot, int prestige) =>
        isBot || !Enum.IsDefined(requested) || !Cosmetics.IsUnlocked(requested, prestige) ? HatId.None : requested;

    /// <summary>Server: a player asked to wear a hat. Checks it, and tells everyone if it changed.</summary>
    private void ServerSetHat(Player p, HatId requested)
    {
        if (!IsServer)
            return;
        var hat = ValidHat(requested, p.Bot != null, ReportedPrestige(p));
        if (hat == p.Hat)
            return;
        p.Hat = hat;
        BroadcastRoster();
    }

    /// <summary>Reads the hat byte a client appends to its loadout packet (older clients send none: no hat).</summary>
    private void ReadLoadoutHat(Player p, ref PacketReader r) => ServerSetHat(p, r.Has(1) ? (HatId)r.U8() : HatId.None);

    /// <summary>Roster trailer: how many, then id + hat for each player.</summary>
    private void WriteRosterHats()
    {
        _w.U8((byte)Players.Count);
        foreach (var p in Players.Values)
            _w.U8(p.Id).U8((byte)(p.Bot != null ? HatId.None : p.Hat));
    }

    /// <summary>Client: the roster's hat trailer (a roster without one, from an older host: everyone bare-headed).</summary>
    private void ReadRosterHats(ref PacketReader r)
    {
        if (IsServer)
            return;
        _hats.Clear();
        int count = r.Has(1) ? r.U8() : 0;
        for (var i = 0; i < count && r.Has(2); i++)
        {
            var id = r.U8();
            var hat = (HatId)r.U8();
            if (!Enum.IsDefined(hat))
                hat = HatId.None; // a newer or hostile host: draw nothing rather than garbage
            _hats[id] = hat;
        }
        foreach (var p in Players.Values)
            p.Hat = _hats.GetValueOrDefault(p.Id);
    }

    // ---- Self-test hooks ---------------------------------------------------------------------

    /// <summary>Self-test: the roster packet the server sends right now (with its hat trailer).</summary>
    public byte[] SelfTestRosterPacket()
    {
        BroadcastRoster();
        return _w.Span.ToArray();
    }

    /// <summary>Self-test: the server's hat check for a player, as if their loadout packet asked for <paramref name="hat"/>.</summary>
    public void SelfTestRequestHat(Player p, HatId hat) => ServerSetHat(p, hat);
}
