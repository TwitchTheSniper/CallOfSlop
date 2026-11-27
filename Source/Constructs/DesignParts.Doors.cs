namespace CallOfSlop;

/// <summary>
/// Design parts, the doors (DesignParts.cs has the map of the files): the sockets' doors, airlocks and hatches, and the rooms' doors (the plan's package E). A door is a box-only element on segment 0 (ConstructCollider.MovePart moves nothing else) with <see cref="Element.Door"/> set: <see cref="PartOf"/> copies it onto the def as an event door (PartRole.Door). Its settings (open, locked) are a parsed door template's: give the kind an Fn whose template is a part file with a "door" block (DesignParts.Templates.cs), so ConstructConfig adds them.
/// </summary>
public static partial class DesignParts
{
    /// <summary>The doors' elements (none yet): after the rooms, so a door can weld to its room's and its socket's.</summary>
    private static IEnumerable<int> AddDoors(Steps st)
    {
        yield break;
    }

    /// <summary>This file's kinds (none yet).</summary>
    private static void RegisterDoors(KindTable k)
    {
    }
}
