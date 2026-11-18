namespace CallOfSlop;

/// <summary>A player's cosmetic profile as it travels in the roster: look (Appearance.cs), uniform and spacewalk gear.</summary>
public readonly record struct PlayerCosmetics(PlayerLook Look = default, byte Uniform = 0, byte Eva = 0);

/// <summary>
/// Looks on the network. Cosmetic only: nothing here touches gameplay. The roster carries each player's look, uniform and
/// spacewalk gear (<see cref="PlayerLook.WireBytes"/> + 2 bytes a player), so a joiner sees everyone's APPEARANCE picks from
/// the roster it gets on joining. (Prestige, the account level and the prestige hats that went before them were removed
/// with levelling, protocol 345.)
/// </summary>
public sealed partial class Game
{
    private readonly Dictionary<byte, PlayerCosmetics> _cosmetics = new();

    private static void WriteRosterCosmetics(PacketWriter w, Player p)
    {
        (p.Bot != null ? default : p.Look).Pack(w); // protocol 45: the look (Appearance.cs; bots: Auto)
        w.U8(p.Uniform); // protocol 257: the uniform the server dresses them in (GameUniforms.cs; bots too: the colony's staff)
        w.U8(p.Eva); // protocol 336: their spacewalk gear (a space suit, an MMU on it: GameSandboxEva.cs)
    }

    private static PlayerCosmetics ReadRosterCosmetics(ref PacketReader r, bool isBot)
    {
        var look = PlayerLook.Unpack(ref r); // sanitised: an unknown variant draws as Auto
        var uniform = r.Has(1) ? r.U8() : (byte)0;
        if (Uniforms.Get(uniform) == null)
            uniform = 0; // (a uniform this build doesn't have: plain clothes)
        var eva = r.Has(1) ? (byte)(r.U8() & (Eva.Suit | Eva.Mmu)) : (byte)0;
        return isBot ? new PlayerCosmetics(default, uniform, eva) : new PlayerCosmetics(look, uniform, eva);
    }

    private static void ApplyCosmetics(Player p, in PlayerCosmetics c)
    {
        p.Uniform = c.Uniform;
        if ((c.Eva & ~p.Eva & Eva.Suit) != 0)
            p.SuitAir = Eva.SuitSeconds; // (a suit just put on is full: a joined client's HUD keeps with the server's locker)
        p.Eva = c.Eva;
        if (p.Bot != null)
            return;
        p.Look = c.Look;
    }

    /// <summary>
    /// Server: puts a look on a player (people only; bots keep the name's look). Values this build doesn't know become Auto.
    /// Tells everyone (the roster) when it changes. Returns the look actually worn.
    /// </summary>
    public PlayerLook ServerSetLook(Player p, PlayerLook look)
    {
        if (!IsServer)
            return p.Look;
        var worn = p.Bot == null ? look.Sanitized() : default;
        if (worn != p.Look)
        {
            p.Look = worn;
            BroadcastRoster();
        }
        return worn;
    }
}
