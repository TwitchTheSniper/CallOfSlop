namespace CallOfSlop;

/// <summary>
/// One player's Sandbox inventory (milestone 3, salvage; docs/sandbox/CONSTRUCTS.md "Salvage and the inventory"): how
/// many of each construct part they carry (by part id), their scrap, and room for materials (resources come later). The
/// server owns it; each player's own copy reaches their client in a compact <see cref="CBodyKind.Inventory"/> message
/// whenever it changes. It resets with the match, like the constructs (nothing is saved yet). Fixed arrays: nothing
/// allocates once it exists.
/// </summary>
public sealed class SandboxInventory
{
    public const int MaxKinds = ConstructParts.MaxTypes; // part ids (two bytes since protocol 87)
    public const int MaxMaterials = 16; // (resources: a later milestone; always zero now, but the wire carries them)
    public const ushort MaxCount = 9999; // of one kind
    public const int MaxScrap = 999_999;

    /// <summary>Counts by construct part id (<see cref="ConstructParts"/>).</summary>
    public readonly ushort[] Parts = new ushort[MaxKinds];

    /// <summary>Counts by material id (none exist yet).</summary>
    public readonly ushort[] Materials = new ushort[MaxMaterials];

    public int Scrap;

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

    /// <summary>Takes up to <paramref name="n"/> scrap out (physical storage: tipped into a bin); returns how much.</summary>
    public int TakeScrap(int n)
    {
        n = Math.Clamp(n, 0, Scrap);
        if (n == 0)
            return 0;
        Scrap -= n;
        Version++;
        return n;
    }

    public void AddScrap(int n)
    {
        if (n <= 0)
            return;
        Scrap = Math.Min(MaxScrap, Scrap + n);
        Version++;
    }

    public void Clear()
    {
        Array.Clear(Parts);
        Array.Clear(Materials);
        Scrap = 0;
        Version++;
    }

    /// <summary>
    /// The scrap a wrecked part gives by its mass and material (a part file's <c>"scrap"</c> overrides it): a fifth of a
    /// point a kilogram of steel (a 30 kg engine: 6), a tenth for rubber or wood, a twentieth for concrete, 0.15 for
    /// anything else; at least 1.
    /// </summary>
    public static int ScrapFrom(float mass, string material)
    {
        var perKg = material switch
        {
            "steel" => 0.2f,
            "rubber" or "wood" => 0.1f,
            "concrete" => 0.05f,
            _ => 0.15f,
        };
        return Math.Max(1, (int)MathF.Round(mass * perKg));
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
