namespace CallOfSlop;

/// <summary>
/// One player's Sandbox inventory (milestone 3, salvage; docs/sandbox/CONSTRUCTS.md "Salvage and the inventory"): how
/// many of each construct part they carry (by part id), the bulk materials they carry (a load taken from a bin; RecyclingTable ids). The
/// server owns it; each player's own copy reaches their client in a compact <see cref="CBodyKind.Inventory"/> message
/// whenever it changes. It resets with the match, like the constructs (nothing is saved yet). Fixed arrays: nothing
/// allocates once it exists.
/// </summary>
public sealed class SandboxInventory
{
    public const int MaxKinds = ConstructParts.MaxTypes; // part ids (two bytes since protocol 90)
    public const int MaxMaterials = 16; // (slot 0 is unused: RecyclingTable ids are 1-based)
    public const ushort MaxCount = 9999; // of one kind

    /// <summary>Counts by construct part id (<see cref="ConstructParts"/>).</summary>
    public readonly ushort[] Parts = new ushort[MaxKinds];

    /// <summary>Counts by material id (RecyclingTable; carried loads taken from bins).</summary>
    public readonly ushort[] Materials = new ushort[MaxMaterials];

    /// <summary>Building takes parts from the inventory only when this is off (the map's <c>infiniteParts</c>; a client hears it with its counts).</summary>
    public bool Infinite = true;

    /// <summary>Goes up on every change, so a window can rebuild its text only when something changed.</summary>
    public int Version;

    public int Count(ushort part) => Parts[part];

    /// <summary>How many parts in all (every kind).</summary>
    public int TotalParts
    {
        get
        {
            var n = 0;
            foreach (var c in Parts)
                n += c;
            return n;
        }
    }

    /// <summary>How many kinds of part it holds at least one of.</summary>
    public int Kinds
    {
        get
        {
            var n = 0;
            foreach (var c in Parts)
                if (c > 0)
                    n++;
            return n;
        }
    }

    public bool Add(ushort part, int n = 1)
    {
        if (part == ConstructParts.None || n <= 0 || Parts[part] >= MaxCount)
            return false;
        Parts[part] = (ushort)Math.Min(MaxCount, Parts[part] + n);
        Version++;
        return true;
    }

    public bool Take(ushort part)
    {
        if (part == ConstructParts.None || Parts[part] == 0)
            return false;
        Parts[part]--;
        Version++;
        return true;
    }

    /// <summary>Takes up to <paramref name="n"/> of a material out (tipped into a bin); returns how much.</summary>
    public int TakeMaterial(byte id, int n)
    {
        if (id == 0 || id >= MaxMaterials)
            return 0;
        n = Math.Clamp(n, 0, Materials[id]);
        if (n == 0)
            return 0;
        Materials[id] -= (ushort)n;
        Version++;
        return n;
    }

    /// <summary>Adds up to <paramref name="n"/> of a material (to the cap); returns how much went in.</summary>
    public int AddMaterial(byte id, int n)
    {
        if (id == 0 || id >= MaxMaterials || n <= 0)
            return 0;
        n = Math.Min(n, MaxCount - Materials[id]);
        if (n <= 0)
            return 0;
        Materials[id] += (ushort)n;
        Version++;
        return n;
    }

    /// <summary>How many units of material the player carries in all.</summary>
    public int TotalMaterials
    {
        get
        {
            var n = 0;
            foreach (var c in Materials)
                n += c;
            return n;
        }
    }

    public void Clear()
    {
        Array.Clear(Parts);
        Array.Clear(Materials);
        Version++;
    }
}

/// <summary>Every player's inventory (by player id) and which changed since they were last sent.</summary>
public sealed class SandboxInventories
{
    private readonly SandboxInventory?[] _of = new SandboxInventory?[256];
    private readonly int[] _sent = new int[256];

    /// <summary>A player's inventory (made the first time it is asked for; empty until something goes in).</summary>
    public SandboxInventory Of(byte player) => _of[player] ??= new SandboxInventory { Infinite = _infinite };

    private bool _infinite = true;

    /// <summary>Has the player's inventory changed since <see cref="MarkSent"/>?</summary>
    public bool Changed(byte player) => _of[player] is { } inv && inv.Version != _sent[player];

    public void MarkSent(byte player) => _sent[player] = _of[player]?.Version ?? 0;

    /// <summary>A new match (or a player gone): empty again, and sent again.</summary>
    public void Clear(byte player)
    {
        if (_of[player] is { } inv)
        {
            inv.Clear();
            _sent[player] = inv.Version - 1;
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _of.Length; i++)
            Clear((byte)i);
    }

    /// <summary>The rules flag on every inventory (the server sets it from the map; it travels with the counts).</summary>
    public void SetInfinite(bool infinite)
    {
        _infinite = infinite;
        foreach (var inv in _of)
            if (inv != null && inv.Infinite != infinite)
            {
                inv.Infinite = infinite;
                inv.Version++;
            }
    }
}
