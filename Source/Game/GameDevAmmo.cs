namespace CallOfSlop;

/// <summary>
/// The host's dev "Infinite ammo" (F7 weapon tweak window, for testing explosions and craters): guns never run dry or
/// reload and grenades and tactical equipment are never used up, for everyone, in any mode. Told to clients in the lobby
/// state's Zombies flags byte (bit 2, protocol 109), which goes out at once in every phase, so it applies mid-match.
/// It is the same path as Zombies' Infinite Ammo power-up (<see cref="InfiniteAmmoOn"/>).
/// </summary>
public sealed partial class Game
{
    private bool _devInfiniteAmmo;

    /// <summary>The host's dev Infinite ammo (clients: as the host last said).</summary>
    public bool DevInfiniteAmmo
    {
        get => _devInfiniteAmmo;
        set
        {
            if (value == _devInfiniteAmmo)
                return;
            _devInfiniteAmmo = value;
            if (IsServer)
                MarkLobbyDirty();
        }
    }

    /// <summary>Lobby state flag bit for <see cref="DevInfiniteAmmo"/> (the Zombies flags byte; bit 1 is the test kit).</summary>
    private const byte LobbyZFlagDevAmmo = 2;
}
