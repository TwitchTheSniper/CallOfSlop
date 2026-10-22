using System.Numerics;

namespace CallOfSlop;

/// <summary>One static box of a building's shell (walls, floors, roof, stairs, trim) with its colour and texture layer.</summary>
public readonly record struct BuildingBox(Box Box, Vector3 Color, int Layer);

/// <summary>A door as the generator made it (world space).</summary>
public sealed class PlannedDoor
{
    /// <summary>The closed leaf: what blocks bodies and bullets while the door is shut.</summary>
    public Box Closed;

    /// <summary>The hinge (bottom of the leaf's hinge edge, on the wall's centre line).</summary>
    public Vector3 Hinge;

    /// <summary>Unit direction from the hinge along the closed leaf, and along the fully open leaf (into the room it swings into).</summary>
    public Vector2 ClosedDir, OpenDir;

    public float Width, Height, Thickness;
    public DoorMaterial Material;
    public int Health;
    public Vector3 Color;
    public bool StartOpen, Front;
    public int Floor;

    /// <summary>Points 0.9 m out from the door on the side it swings into, and on the other side (feet height).</summary>
    public Vector3 SwingSide, OtherSide;
}

/// <summary>A window's glass as the generator made it (world space).</summary>
public sealed class PlannedWindow
{
    public Box Glass;
    public int Health;
    public int Floor;
    public float Sill;
    public bool Exterior;
}

/// <summary>A prop placed by the building (furniture, exterior props).</summary>
public readonly record struct PlannedProp(PropDef Def, Vector3 Position, float Yaw, float Scale, string Room);

/// <summary>A room as placed (world space): for the self-test and tools.</summary>
public sealed class PlannedRoom
{
    public required string Id, Type;
    public int Floor;
    public float FloorY, CeilingY;
    public readonly List<Box> Inside = new(); // the room's floor area (inside the walls), one box per rect, floor to ceiling
    public readonly List<Box> DoorZones = new(); // kept clear of solid furniture
    public Vector3 Waypoint;
    public int Placed, Skipped; // furnishing items placed and items that found no room
}

/// <summary>Everything one Building op makes, in world space. <see cref="BuildingGen.Generate"/> builds it.</summary>
public sealed class BuildingPlan
{
    public required BuildingDef Def;
    public readonly List<BuildingBox> Shell = new();
    public readonly List<PlannedDoor> Doors = new();
    public readonly List<PlannedWindow> Windows = new();
    public readonly List<PlannedProp> Props = new();
    public readonly List<Vector3> Waypoints = new();
    public readonly List<(string Type, string Name, Vector3 At)> Anchors = new();
    public readonly List<PlannedRoom> Rooms = new();
    public readonly List<(Vector3 Bottom, Vector3 Top)> Stairs = new();

    /// <summary>The building's outline on the ground (outer wall faces), world space, and its bounds.</summary>
    public readonly List<BRect> Footprint = new();

    public Box Bounds;
    public int Floors;
    public float BaseY, RoofY;

    /// <summary>
    /// Ways between rooms ("floor:id" keys, "outside" for out of the building): doors, arches, low holes, open-plan joins and
    /// stairs. The self-test walks them from <see cref="FrontRoom"/> to check every room can be reached.
    /// </summary>
    public readonly List<(string A, string B)> Links = new();

    /// <summary>The room the front door (or else the first ground-floor exterior door) opens into, or null.</summary>
    public string? FrontRoom;

    /// <summary>Non-fatal notes (furniture that didn't fit), for the log and tools.</summary>
    public readonly List<string> Notes = new();
}
