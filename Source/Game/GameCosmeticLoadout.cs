namespace CallOfSlop;

/// <summary>
/// The look from the APPEARANCE screen on its way to the server: after the kill streaks in <see cref="Msg.Loadout"/>, sent
/// on joining and with every Deploy / Respawn; the roster tells everyone (GameCosmetics.cs). (The hat byte and the prestige
/// and level after it were removed with levelling, protocol 345.)
/// </summary>
public sealed partial class Game
{
    private PlayerLook _chosenLook;

    /// <summary>
    /// The look picked on the APPEARANCE screen (<see cref="AppearanceStore.Look"/>), sent after the profile in every loadout
    /// packet. On the host it goes on the local player at once. Same rules everywhere (free-for-all, team modes, co-op, Zombies).
    /// </summary>
    public PlayerLook ChosenLook
    {
        get => _chosenLook;
        set
        {
            _chosenLook = value.Sanitized();
            if (IsServer && Local is { } me)
                ServerSetLook(me, _chosenLook);
        }
    }

    /// <summary>Server: reads the look a client appends to its loadout packet (<see cref="PlayerLook.WireBytes"/> bytes).</summary>
    private void ReadLoadoutLook(Player p, ref PacketReader r)
    {
        if (r.Has(PlayerLook.WireBytes))
            ServerSetLook(p, PlayerLook.Unpack(ref r));
    }

    /// <summary>The look player <paramref name="id"/> wears, as far as this side knows (Auto if unknown).</summary>
    public PlayerLook LookOf(byte id) =>
        Players.TryGetValue(id, out var p) ? p.Look : _cosmetics.TryGetValue(id, out var c) ? c.Look : default;
}
