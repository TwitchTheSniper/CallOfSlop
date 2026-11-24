namespace CallOfSlop;

/// <summary>
/// The world's ship gravity rule (docs/sandbox/SHIP-GRAVITY.md, ShipGravity.cs): chosen when the world is made and kept with
/// it, like its death rules. The host sends it in the Welcome and the lobby state, so a client's prediction steps riders by the
/// same rule. Server, each tick per person: they are watched for cargo near them (0 g settling), and hurt past the g limit.
/// </summary>
public sealed partial class Game
{
    /// <summary>The rule in force (server: the settings or the world's own; client: as last heard).</summary>
    public ShipGravityMode SandboxShipGravity => ShipGravity.Mode;

    private readonly byte[] _sgHurt = new byte[256];

    /// <summary>Server: the rule as the game starts (the session's settings). Clients hear it instead.</summary>
    public void SetSandboxShipGravity(ShipGravityMode m)
    {
        if (IsServer)
            ShipGravity.Mode = m;
    }

    /// <summary>Host, in the lobby: the rule for the next match. A saved world's is its own.</summary>
    public void LobbySetSandboxShipGravity(ShipGravityMode m)
    {
        if (!IsServer || !InLobby || _lobbyCountdownUntil > 0 || WorldSession != null || m == ShipGravity.Mode)
            return;
        ShipGravity.Mode = m;
        MarkLobbyDirty();
    }

    /// <summary>Client: what the host's Welcome or lobby state says.</summary>
    private static void HearSandboxShipGravity(int b) => ShipGravity.Mode = (ShipGravityMode)Math.Min(b, (int)ShipGravityMode.Everywhere);

    /// <summary>Server, each tick for every player in Sandbox: watched for cargo, and hurt by more g than they are braced for.</summary>
    private void ShipGravityTick(Player p, in SandboxHand h, double now)
    {
        p.FeltPull = ShipGravity.PersonPull(p.Sim, Constructs, h.SeatBody, p.Alive); // (the felt-g readout: the owner's snapshot carries it, UiVitalsPanel.cs)
        if (ShipGravity.Mode == ShipGravityMode.Deck || !p.Alive)
            return;
        ShipGravity.Watch(ServerTick, p.Position);
        if (p.Bot != null || p.InShell)
            return;
        ref var wait = ref _sgHurt[p.Id];
        if (wait > 0)
        {
            wait--;
            return;
        }
        wait = (byte)(World.TickRate - 1);
        var g = ShipGravity.FeltG(p.Sim, Constructs, h.SeatBody, h.SeatPart, out var limit, (p.Eva & Eva.Suit) != 0);
        if (g <= limit)
            return;
        var hurt = (int)MathF.Ceiling((g - limit) * ShipGravity.HarmPerG);
        if (DamagePlayer(p, p, hurt, false, SuicideKillId, now, DamageKind.Melee))
            ServerSystemChat($"{p.Name} was crushed by {g:0} g", now);
    }
}
