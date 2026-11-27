namespace CallOfSlop;

/// <summary>
/// Design parts, the fixed systems (DesignParts.cs has the map of the files): tow points, floodlights, docking clamps, grapples, fixed guns and their directors, turrets and hangars (the plan's packages G and H). A piece kind's work goes in <see cref="KindTable.Piece"/>, its def's start and finish in <see cref="KindTable.Fn"/>, and a wire between two elements in <see cref="Element.Wires"/> (BuildSteps lays it).
/// </summary>
public static partial class DesignParts
{
    /// <summary>The systems' own elements past their pieces (none yet: a hidden Fire Director per gunner seat goes here).</summary>
    private static IEnumerable<int> AddSystems(Steps st)
    {
        yield break;
    }

    /// <summary>This file's kinds (none yet).</summary>
    private static void RegisterSystems(KindTable k)
    {
    }
}
