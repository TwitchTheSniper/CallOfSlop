namespace CallOfSlop;

/// <summary>
/// Design parts, the risers (DesignParts.cs has the map of the files): ladders and lifts (the plan's package F). A ladder is an element with <see cref="Element.LadderTop"/> (and its foot); a lift is a piston: its carriage element has <see cref="Element.Joint"/>, and what rides it has <see cref="Element.Seg"/> set to that joint's number.
/// </summary>
public static partial class DesignParts
{
    /// <summary>The risers' elements (none yet).</summary>
    private static IEnumerable<int> AddRisers(Steps st)
    {
        yield break;
    }

    /// <summary>This file's kinds (none yet).</summary>
    private static void RegisterRisers(KindTable k)
    {
    }
}
