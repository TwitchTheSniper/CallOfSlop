namespace CallOfSlop;

/// <summary>
/// Player factions in Sandbox (docs/sandbox/CONSTRUCTS.md, "Factions"): a group of players who share what they own.
/// Decision 19 said "the owner is the faction" until factions existed; now a construct (or a beacon) is treated as your own
/// when its owner is you or anyone in your faction (<see cref="Factions.Shares"/>): you may drive, edit, configure, grab,
/// take apart and use its beacon's network as if you had built it, and it is never "defended" against you. Ownership itself
/// stays with one player (<see cref="Construct.Owner"/>), who may give it to another member (GameSandboxFactions.cs).
/// <para>
/// The table is keyed by owner byte, the same byte <see cref="Construct.Owner"/> holds: a player's id while they're here,
/// their record's offline byte while they're away from a saved world (<see cref="WorldSession.OfflineBase"/> and up), so
/// what they own stays shared while they're gone. One table per <see cref="Game"/> (<see cref="WorldContext"/> swaps
/// <see cref="Factions.Now"/>); the server owns it and sends it whole (Msg.Factions) when it changes.
/// </para>
/// </summary>
public sealed class FactionTable
{
    /// <summary>Faction ids are 1 to Max - 1 (0: none).</summary>
    public const int Max = 32;

    /// <summary>Longest faction name (ASCII).</summary>
    public const int NameMax = 24;

    /// <summary>Owner byte -> faction id (0: none).</summary>
    public readonly byte[] Of = new byte[256];

    /// <summary>Each faction's name by id ("": a free id).</summary>
    public readonly string[] Names = new string[Max];

    /// <summary>Each faction's leader, an owner byte (255: none). The leader invites and removes members.</summary>
    public readonly byte[] Leader = new byte[Max];

    /// <summary>Player id -> the faction they're invited to (0: none). An invite is for this session only.</summary>
    public readonly byte[] Invited = new byte[256];

    /// <summary>A member's name by owner byte (for members who are away; the players' list has the rest).</summary>
    public readonly string[] MemberName = new string[256];

    /// <summary>Bumped on every change (the window redraws its lists; the server sends the table).</summary>
    public int Version;

    public FactionTable() => Clear();

    public void Clear()
    {
        Array.Clear(Of);
        Array.Clear(Invited);
        Array.Fill(Names, "");
        Array.Fill(Leader, (byte)255);
        Array.Fill(MemberName, "");
        Version++;
    }

    /// <summary>True when <paramref name="owner"/>'s things are <paramref name="player"/>'s to use: the same player, or both in one faction.</summary>
    public bool Shares(byte owner, byte player) => owner == player || owner != 255 && player != 255 && Of[owner] != 0 && Of[owner] == Of[player];

    /// <summary>How many owner bytes are in faction <paramref name="id"/>.</summary>
    public int Count(byte id)
    {
        if (id == 0)
            return 0;
        var n = 0;
        foreach (var f in Of)
            if (f == id)
                n++;
        return n;
    }

    /// <summary>A free faction id, or 0 when every one is taken.</summary>
    public byte FreeId()
    {
        for (var id = 1; id < Max; id++)
            if (Names[id].Length == 0)
                return (byte)id;
        return 0;
    }

    /// <summary>The faction called <paramref name="name"/> (any case), or 0.</summary>
    public byte Find(string name)
    {
        for (var id = 1; id < Max; id++)
            if (Names[id].Length > 0 && string.Equals(Names[id], name, StringComparison.OrdinalIgnoreCase))
                return (byte)id;
        return 0;
    }

    /// <summary>A faction name as typed, made safe: printable ASCII, trimmed, at most <see cref="NameMax"/> characters.</summary>
    public static string CleanName(string s)
    {
        Span<char> b = stackalloc char[NameMax];
        var n = 0;
        foreach (var ch in s)
            if (ch is >= ' ' and <= '~' && n < NameMax)
                b[n++] = ch;
        return new string(b[..n]).Trim();
    }

    /// <summary>
    /// Owner byte <paramref name="from"/> becomes <paramref name="to"/> (a player leaving a saved world, or coming back):
    /// their faction and leadership go with them.
    /// </summary>
    public void Move(byte from, byte to, string name)
    {
        if (from == to || from == 255 || to == 255)
            return;
        Of[to] = Of[from];
        Of[from] = 0;
        MemberName[to] = name;
        MemberName[from] = "";
        for (var id = 1; id < Max; id++)
            if (Leader[id] == from)
                Leader[id] = to;
        Version++;
    }

    /// <summary>
    /// Owner byte <paramref name="who"/> leaves their faction: a leader hands over to another member (one who is here
    /// first, by <paramref name="here"/>), and the last one out ends the faction. The faction id it was in, or 0.
    /// </summary>
    public byte Leave(byte who, Func<byte, bool>? here = null)
    {
        var id = Of[who];
        if (id == 0)
            return 0;
        Of[who] = 0;
        MemberName[who] = "";
        if (Leader[id] == who)
        {
            Leader[id] = 255;
            for (var pass = 0; pass < 2 && Leader[id] == 255; pass++)
                for (var k = 0; k < 255; k++)
                    if (Of[k] == id && (pass == 1 || here?.Invoke((byte)k) == true))
                    {
                        Leader[id] = (byte)k;
                        break;
                    }
        }
        if (Count(id) == 0)
        {
            Names[id] = "";
            Leader[id] = 255;
            for (var k = 0; k < Invited.Length; k++)
                if (Invited[k] == id)
                    Invited[k] = 0;
        }
        Version++;
        return id;
    }
}

/// <summary>The entered world's <see cref="FactionTable"/> (<see cref="WorldContext"/> swaps it), for the ownership checks.</summary>
public static class Factions
{
    /// <summary>The table of the Game whose world is entered.</summary>
    public static FactionTable Now { get; internal set; } = new();

    /// <summary>True when <paramref name="owner"/>'s things are <paramref name="player"/>'s to use: the same player, or a faction mate.</summary>
    public static bool Shares(byte owner, byte player) => owner == player || Now.Shares(owner, player);
}
