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
    public const int MaxMaterials = 64; // (slot 0 is unused: RecyclingTable ids are 1-based)
    public const ushort MaxCount = 9999; // of one kind

    /// <summary>Counts by construct part id (<see cref="ConstructParts"/>).</summary>
    public readonly ushort[] Parts = new ushort[MaxKinds];

    /// <summary>Counts by material id (RecyclingTable; carried loads taken from bins).</summary>
    public readonly ushort[] Materials = new ushort[MaxMaterials];

    private readonly float[] _bits = new float[MaxMaterials]; // what was dug short of a whole unit, by material (server only)

    /// <summary>Building takes parts from the inventory only when this is off (the map's <c>infiniteParts</c>; a client hears it with its counts).</summary>
    public bool Infinite = true;

    /// <summary>Goes up on every change, so a window can rebuild its text only when something changed.</summary>
    public int Version;

    public int Count(ushort part) => part < Parts.Length ? Parts[part] : 0; // (an extrusion's run-time id is off the table: none are counted)

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
        if (part == ConstructParts.None || part >= Parts.Length || n <= 0 || Parts[part] >= MaxCount)
            return false;
        Parts[part] = (ushort)Math.Min(MaxCount, Parts[part] + n);
        Version++;
        return true;
    }

    public bool Take(ushort part)
    {
        if (part == ConstructParts.None || part >= Parts.Length || Parts[part] == 0)
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

    /// <summary>
    /// Adds <paramref name="units"/> of a material dug out of the ground, keeping the fraction for the next scoop (server only:
    /// the wire and the HUD carry whole units), so what a stroke of digging gives doesn't depend on how it was cut up. Returns the whole units that went in.
    /// </summary>
    public int AddBulk(byte id, float units)
    {
        if (id == 0 || id >= MaxMaterials || !(units > 0))
            return 0;
        var sum = _bits[id] + units;
        var whole = (int)sum;
        _bits[id] = sum - whole;
        return AddMaterial(id, whole);
    }

    /// <summary>How much of a material the player can pay for ground put back, the fraction included.</summary>
    public float BulkOf(byte id) => id == 0 || id >= MaxMaterials ? 0 : Materials[id] + _bits[id];

    /// <summary>Pays <paramref name="units"/> of a material (the caller checked <see cref="BulkOf"/>), keeping the fraction.</summary>
    public void PayBulk(byte id, float units)
    {
        if (id == 0 || id >= MaxMaterials || !(units > 0))
            return;
        var left = _bits[id] - units;
        var whole = left < 0 ? (int)MathF.Ceiling(-left) : 0;
        var taken = TakeMaterial(id, whole);
        _bits[id] = MathF.Max(left + taken, 0);
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
        Array.Clear(_bits);
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
