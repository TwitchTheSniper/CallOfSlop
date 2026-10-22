namespace CallOfSlop;

/// <summary>
/// Where the cosmetic loadout reads the local prestige from (<see cref="Progress.Prestige"/>). One place, so the loadout
/// screen and the save code don't care how prestige is stored.
/// </summary>
public static class CosmeticPrestige
{
    /// <summary>The prestige (0..<see cref="Cosmetics.MaxPrestige"/>) of a local profile (null: 0).</summary>
    public static int Of(Progress? progress) => Math.Clamp(progress?.Prestige ?? 0, 0, Cosmetics.MaxPrestige);
}

/// <summary>
/// The hat from the loadout screen on its way to the server (the network side of prestige and hats is GameCosmetics.cs):
/// <list type="bullet">
/// <item>client -> server: one byte after the kill streaks in <see cref="Msg.Loadout"/> (older clients send none: no hat),
/// sent on joining and with every Deploy / Respawn;</item>
/// <item>the server keeps it only for a person whose reported prestige unlocks it (<see cref="ServerSetHat"/>; bots never),
/// and the roster tells everyone (<see cref="Player.Hat"/>, drawn by the hat renderer).</item>
/// </list>
/// </summary>
public sealed partial class Game
{
    private HatId _chosenHat;

    /// <summary>The hat picked on the loadout screen (the profile's, <see cref="LoadoutStore.Hat"/>). On the host it goes on the local player at once.</summary>
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

    /// <summary>
    /// The hat the server lets a player wear: bots never wear one, people only a known hat their prestige unlocks; anything
    /// else is None. The same rule as <see cref="ServerSetHat"/>, pure (checked by the self-test).
    /// </summary>
    public static HatId ValidHat(HatId requested, bool isBot, int prestige) =>
        isBot || !Cosmetics.IsUnlocked(requested, prestige) ? HatId.None : requested;

    /// <summary>Server: reads the hat byte a client appends to its loadout packet (older clients send none: no hat).</summary>
    private void ReadLoadoutHat(Player p, ref PacketReader r) => ServerSetHat(p, r.Has(1) ? (HatId)r.U8() : HatId.None);

    /// <summary>The hat player <paramref name="id"/> wears, as far as this side knows (None if unknown).</summary>
    public HatId HatOf(byte id) =>
        Players.TryGetValue(id, out var p) ? p.Hat : _cosmetics.TryGetValue(id, out var c) ? c.Hat : HatId.None;

    // ---- Self-test hooks ---------------------------------------------------------------------

    /// <summary>Self-test: the roster packet the server sends right now.</summary>
    public byte[] SelfTestRosterPacket()
    {
        BroadcastRoster();
        return _w.Span.ToArray();
    }
}
