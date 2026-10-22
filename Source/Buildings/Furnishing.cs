namespace CallOfSlop;

/// <summary>How a furnishing item is placed in a room (see Buildings/README.md, "Furnishing rules").</summary>
public enum FurnishPlace : byte
{
    /// <summary>Back against a wall, facing into the room.</summary>
    Wall,

    /// <summary>Back against a wall, tucked into a corner.</summary>
    Corner,

    /// <summary>In the middle of the room.</summary>
    Centre,

    /// <summary>Anywhere on the free floor.</summary>
    Floor,

    /// <summary>On top of an earlier item (a lamp on a bedside table, a monitor on a desk).</summary>
    On,

    /// <summary>Next to an earlier item along its wall (bedside tables by a bed).</summary>
    Beside,

    /// <summary>In front of an earlier item, facing it (a chair at a desk, a TV facing a sofa).</summary>
    Front,

    /// <summary>Round an earlier item, facing it (chairs round a table).</summary>
    Around,

    /// <summary>Hanging from the ceiling (a ceiling fan, a lamp).</summary>
    Ceiling,
}

public enum FurnishPrefer : byte { Any, Far, Near }

/// <summary>One line of a furnishing rule: which props, how many, and where.</summary>
public sealed class FurnishItem
{
    public string Id = "";
    public required string[] Props;
    public FurnishPlace Place;
    public int CountMin = 1, CountMax = 1;
    public float Chance = 1f;
    public string Of = "";
    public float? Y; // "on": the surface height; "ceiling": the gap under the ceiling
    public FurnishPrefer Prefer;
    public float Gap = 0.08f;
    public float Distance = 0.1f; // "front" / "around": the gap to the item
    public float MinArea, MaxArea = 1e9f;
    public float Turn; // degrees added to the facing
    public float Scale = 1f;
}

/// <summary>A furnishing rule (Buildings/Furnishing/&lt;type&gt;.json): what a room of one type gets, in order.</summary>
public sealed class FurnishRule
{
    public required string Type;
    public string Description = "";
    public string? SourceFile;
    public readonly List<FurnishItem> Items = new();
    public uint FileHash;
}
