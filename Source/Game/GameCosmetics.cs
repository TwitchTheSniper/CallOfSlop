namespace CallOfSlop;

/// <summary>A player's cosmetic profile as it travels in the roster: prestige, account level and hat.</summary>
public readonly record struct PlayerCosmetics(byte Prestige, byte Level, HatId Hat);

/// <summary>
/// Prestige and hats on the network (Source/Cosmetics/Cosmetics.cs). Cosmetic only: nothing here touches gameplay.
/// A client reports its prestige and level after its name in <see cref="Msg.Hello"/>; the server believes it only for
/// what it shows (the scoreboard's "P2 L13") and to check which hats that player may wear. The roster carries
/// prestige, level and hat for everyone (3 bytes a player). Bots never have a prestige, a level tag or a hat.
/// </summary>
public sealed partial class Game
{
    private readonly Dictionary<byte, PlayerCosmetics> _cosmetics = new();

    /// <summary>The local profile appended to the Hello: prestige, then level.</summary>
    private void WriteHelloProfile(PacketWriter w)
    {
        var prog = Progress;
        w.U8((byte)Math.Clamp(prog?.Prestige ?? 0, 0, Cosmetics.MaxPrestige)).U8((byte)Math.Clamp(prog?.Level ?? 1, 1, 255));
    }

    /// <summary>Server: reads the Hello's profile (absent from a bare Hello: 0 / unknown).</summary>
    private void ReadHelloProfile(Player p, ref PacketReader r)
    {
        var prestige = r.Has(1) ? r.U8() : (byte)0;
        var level = r.Has(1) ? r.U8() : (byte)0;
        SetProfile(p, prestige, level);
    }

    private static void SetProfile(Player p, int prestige, int level)
    {
        if (p.Bot != null)
        {
            p.Prestige = p.Level = 0;
            p.Hat = HatId.None;
            return;
        }
        p.Prestige = (byte)Math.Clamp(prestige, 0, Cosmetics.MaxPrestige);
        p.Level = (byte)Math.Clamp(level, 0, CallOfSlop.Progress.MaxLevel);
        if (!Cosmetics.IsUnlocked(p.Hat, p.Prestige))
            p.Hat = HatId.None;
    }

    /// <summary>Server: the host's own player takes its prestige and level from the local progression.</summary>
    private void RefreshHostProfile()
    {
        if (IsServer && Players.TryGetValue(LocalId, out var me) && me.Bot == null && Progress is { } prog)
            SetProfile(me, prog.Prestige, prog.Level);
    }

    private static void WriteRosterCosmetics(PacketWriter w, Player p)
    {
        if (p.Bot != null)
            w.U8(0).U8(0).U8(0);
        else
            w.U8(p.Prestige).U8(p.Level).U8((byte)p.Hat);
    }

    private static PlayerCosmetics ReadRosterCosmetics(ref PacketReader r, bool isBot)
    {
        var prestige = r.Has(1) ? r.U8() : (byte)0;
        var level = r.Has(1) ? r.U8() : (byte)0;
        var hat = r.Has(1) ? (HatId)r.U8() : HatId.None;
        if (isBot)
            return default;
        prestige = (byte)Math.Min((int)prestige, Cosmetics.MaxPrestige);
        if (!Cosmetics.IsUnlocked(hat, prestige))
            hat = HatId.None; // unknown id or not earned: draw nothing
        return new PlayerCosmetics(prestige, level, hat);
    }

    private static void ApplyCosmetics(Player p, in PlayerCosmetics c)
    {
        if (p.Bot != null)
            return;
        p.Prestige = c.Prestige;
        p.Level = c.Level;
        p.Hat = c.Hat;
    }

    /// <summary>
    /// Server: puts a hat on a player if they may wear it (a person, and <see cref="Cosmetics.IsUnlocked"/> for the prestige
    /// they reported); anything else becomes <see cref="HatId.None"/>. Tells everyone (the roster) when it changes.
    /// Returns the hat actually worn. For the loadout code (the hat sent with the loadout).
    /// </summary>
    public HatId ServerSetHat(Player p, HatId hat)
    {
        if (!IsServer)
            return p.Hat;
        if (p.Id == LocalId)
            RefreshHostProfile();
        var worn = p.Bot == null && Cosmetics.IsUnlocked(hat, p.Prestige) ? hat : HatId.None;
        if (worn != p.Hat)
        {
            p.Hat = worn;
            BroadcastRoster();
        }
        return worn;
    }

    /// <summary>Scoreboard tag after a person's name: "  [P2 L13]", or "" for bots and players who didn't report a level.</summary>
    public string RankTag(Player p)
    {
        if (p.Bot != null)
            return "";
        if (p.Id == LocalId && Progress is { } prog)
            return $"  [{CallOfSlop.Progress.RankLabel(prog.Prestige, prog.Level)}]";
        return p.Level > 0 ? $"  [{CallOfSlop.Progress.RankLabel(p.Prestige, p.Level)}]" : "";
    }
}
