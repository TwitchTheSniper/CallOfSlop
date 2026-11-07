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

    /// <summary>Each faction's colour (0xRRGGBB), on its name tags and in the window.</summary>
    public readonly uint[] Colour = new uint[Max];

    /// <summary>Each faction's short tag (1 to <see cref="TagMax"/> letters or digits, upper case), shown before its members' names.</summary>
    public readonly string[] Tag = new string[Max];

    /// <summary>"[TAG] " for the name tags, made when the tag changes (no per-frame text).</summary>
    public readonly string[] TagLabel = new string[Max];

    /// <summary>True when anyone may join without an invite.</summary>
    public readonly bool[] Open = new bool[Max];

    /// <summary>A member's rank by owner byte, an index into <see cref="FactionRanks.Player"/> (the leader's is always the top: <see cref="RankOf"/>).</summary>
    public readonly byte[] Rank = new byte[256];

    /// <summary>Player id -> the faction they asked to join (0: none). For this session only, like an invite.</summary>
    public readonly byte[] Asked = new byte[256];

    /// <summary>
    /// Faction a's stance toward faction b at [a * Max + b]: -1 at war, 0 neutral, 1 wants an alliance. Two factions are
    /// allied when both want it and at war when either says so (<see cref="Relation"/>).
    /// </summary>
    public readonly sbyte[] Stance = new sbyte[Max * Max];

    /// <summary>
    /// How the story's factions see each player faction (server: GameStoryFactions.cs, sent to everyone): their names, and
    /// per player faction a standing from -100 to 100 each (<see cref="StoryStanding"/>[id * MaxStory + f]).
    /// </summary>
    public const int MaxStory = 16;
    public string[] StoryNames = [];
    public readonly sbyte[] StoryStanding = new sbyte[Max * MaxStory];

    /// <summary>A player faction every story faction hunts (it did something none forgives; StoryFactions.Notorious).</summary>
    public readonly bool[] Notorious = new bool[Max];

    /// <summary>Longest tag.</summary>
    public const int TagMax = 5;

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
        Array.Clear(Colour);
        Array.Fill(Tag, "");
        Array.Fill(TagLabel, "");
        Array.Clear(Open);
        Array.Clear(Rank);
        Array.Clear(Asked);
        Array.Clear(Stance);
        Array.Clear(StoryStanding);
        Array.Clear(Notorious);
        Version++;
    }

    /// <summary>Owner byte <paramref name="who"/>'s rank in their faction (the leader's is the top one).</summary>
    public int RankOf(byte who)
    {
        var id = Of[who];
        var set = FactionRanks.Player;
        if (id == 0)
            return 0;
        return Leader[id] == who ? set.Top : Math.Min((int)Rank[who], set.Top - 1);
    }

    /// <summary>True when <paramref name="who"/>'s rank lets them do <paramref name="may"/> in their faction.</summary>
    public bool May(byte who, FactionMay may) => Of[who] != 0 && (FactionRanks.Player.May(RankOf(who)) & may) == may;

    /// <summary>
    /// True when <paramref name="player"/> may take apart what <paramref name="owner"/> owns as their own: it is theirs, or a
    /// mate's and their rank allows it (<see cref="FactionMay.Dismantle"/>).
    /// </summary>
    public bool MayDismantle(byte owner, byte player) => owner == player || Shares(owner, player) && May(player, FactionMay.Dismantle);

    public enum Relations : sbyte { War = -1, Neutral = 0, Allied = 1 }

    /// <summary>How factions <paramref name="a"/> and <paramref name="b"/> stand: allied when both want it, at war when either says so.</summary>
    public Relations Relation(byte a, byte b)
    {
        if (a == 0 || b == 0 || a >= Max || b >= Max || a == b)
            return Relations.Neutral;
        int x = Stance[a * Max + b], y = Stance[b * Max + a];
        return x < 0 || y < 0 ? Relations.War : x > 0 && y > 0 ? Relations.Allied : Relations.Neutral;
    }

    /// <summary>The relation between the factions of two owner bytes (neutral when either is in none or both in one).</summary>
    public Relations RelationOf(byte owner, byte player) => owner == 255 || player == 255 ? Relations.Neutral : Relation(Of[owner], Of[player]);

    /// <summary>Sets a faction's tag (cleaned: <see cref="CleanTag"/>) and its label.</summary>
    public void SetTag(byte id, string tag)
    {
        Tag[id] = CleanTag(tag);
        TagLabel[id] = Tag[id].Length > 0 ? $"[{Tag[id]}] " : "";
    }

    /// <summary>A tag as typed, made safe: letters and digits, upper case, at most <see cref="TagMax"/>.</summary>
    public static string CleanTag(string s)
    {
        Span<char> b = stackalloc char[TagMax];
        var n = 0;
        foreach (var ch in s)
            if (n < TagMax && ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                b[n++] = char.ToUpperInvariant(ch);
        return new string(b[..n]);
    }

    /// <summary>A starting tag from a name: its initials, or its first letters.</summary>
    public static string TagFrom(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Select(w => CleanTag(w).Length > 0 ? CleanTag(w)[..1] : ""));
        return CleanTag(initials.Length >= 2 ? initials : name);
    }

    /// <summary>A starting colour from a name: a bright hue picked by its letters (the leader can change it).</summary>
    public static uint ColourFrom(string name)
    {
        var h = 2166136261u;
        foreach (var ch in name.ToUpperInvariant())
            h = (h ^ ch) * 16777619u;
        var hue = h % 360 / 60f;
        var x = 1 - MathF.Abs(hue % 2 - 1);
        var (r, g, b) = (int)hue switch { 0 => (1f, x, 0f), 1 => (x, 1f, 0f), 2 => (0f, 1f, x), 3 => (0f, x, 1f), 4 => (x, 0f, 1f), _ => (1f, 0f, x) };
        static uint C(float v) => (uint)(80 + v * 175);
        return C(r) << 16 | C(g) << 8 | C(b);
    }

    /// <summary>Forgets a faction's own settings (its id is free again).</summary>
    private void ForgetFaction(byte id)
    {
        Names[id] = "";
        Leader[id] = 255;
        Colour[id] = 0;
        Tag[id] = TagLabel[id] = "";
        Open[id] = false;
        Notorious[id] = false;
        Array.Clear(StoryStanding, id * MaxStory, MaxStory);
        for (var k = 0; k < Max; k++)
            Stance[id * Max + k] = Stance[k * Max + id] = 0;
        for (var k = 0; k < Invited.Length; k++)
        {
            if (Invited[k] == id)
                Invited[k] = 0;
            if (Asked[k] == id)
                Asked[k] = 0;
        }
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
        Rank[to] = Rank[from];
        Rank[from] = 0;
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
        Rank[who] = 0;
        MemberName[who] = "";
        if (Leader[id] == who)
        {
            // (the highest rank takes over, one who is here first)
            Leader[id] = 255;
            var best = -1;
            for (var pass = 0; pass < 2 && Leader[id] == 255; pass++)
                for (var k = 0; k < 255; k++)
                    if (Of[k] == id && (pass == 1 || here?.Invoke((byte)k) == true) && Rank[k] > best)
                        (best, Leader[id]) = (Rank[k], (byte)k);
        }
        if (Count(id) == 0)
            ForgetFaction(id);
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

    /// <summary>True when <paramref name="player"/> may take apart what <paramref name="owner"/> owns: their own, or a mate's when their rank allows it.</summary>
    public static bool MayDismantle(byte owner, byte player) => owner == player || Now.MayDismantle(owner, player);

    /// <summary>Why a faction mate may not take this apart ("your rank (Recruit) can't..."), or null.</summary>
    public static string? RankRefusal(byte owner, byte player) =>
        owner != player && Now.Shares(owner, player) && !Now.MayDismantle(owner, player)
            ? $"your rank ({FactionRanks.Player.RankName(Now.RankOf(player))}) can't take apart your faction's things"
            : null;
}
